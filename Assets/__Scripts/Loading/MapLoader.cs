using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking; //Used in WebGL

public class MapLoader : MonoBehaviour
{
    private static bool _loading = false;
    public static bool Loading
    {
        get => _loading;

        private set
        {
            _loading = value;
            OnLoadingChanged?.Invoke(value);
        }
    }

    public static string LoadingMessage = "";
    public static float Progress;

    public static event Action<bool> OnLoadingChanged;
    public static event Action OnMapLoaded;
    public static event Action OnLoadingFailed;
    public static event Action OnReplayMapPrompt;


    public static Dictionary<string, DifficultyRank> DiffValueFromString = new Dictionary<string, DifficultyRank>
    {
        {"Easy", DifficultyRank.Easy},
        {"Normal", DifficultyRank.Normal},
        {"Hard", DifficultyRank.Hard},
        {"Expert", DifficultyRank.Expert},
        {"ExpertPlus", DifficultyRank.ExpertPlus}
    };


    private IEnumerator LoadMapFileCoroutine(IMapDataLoader loader)
    {
        Loading = true;

        using Task<LoadedMap> loadingTask = loader.GetMap();
        yield return new WaitUntil(() => loadingTask.IsCompleted);
        LoadedMap mapData = loadingTask.Result;

        Debug.Log("Loading complete.");
        LoadingMessage = "Done";

        loader.Dispose();
        UpdateMapInfo(mapData);
    }


    private void LoadMapZip(string directory)
    {
        Loading = true;

        ZipReader zipReader = new ZipReader();
        try
        {
            Debug.Log("Loading map zip.");
            LoadingMessage = "Loading map zip";

            zipReader.Archive = ZipFile.OpenRead(directory);
            StartCoroutine(LoadMapFileCoroutine(zipReader));
        }
        catch(Exception err)
        {
            zipReader.Dispose();

            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Failed to load zip file!");
            Debug.LogWarning($"Unhandled exception loading zip: {err.Message}, {err.StackTrace}.");

            UpdateMapInfo(LoadedMap.Empty);
        }
    }


#if UNITY_WEBGL && !UNITY_EDITOR
    private IEnumerator LoadMapZipWebGLCoroutine(string directory)
    {
        Loading = true;
        LoadingMessage = "Loading zip";

        Debug.Log("Starting web request.");
        using UnityWebRequest uwr = UnityWebRequest.Get(directory);
        yield return uwr.SendWebRequest();

        if(uwr.result == UnityWebRequest.Result.Success)
        {
            ZipReader zipReader = new ZipReader();
            try
            {
                zipReader.ArchiveStream = new MemoryStream(uwr.downloadHandler.data);
                zipReader.Archive = new ZipArchive(zipReader.ArchiveStream, ZipArchiveMode.Read);

                StartCoroutine(LoadMapFileCoroutine(zipReader));
            }
            catch(Exception e)
            {
                Debug.LogWarning($"Failed to read map data with error: {e.Message}, {e.StackTrace}");
                ErrorHandler.Instance.ShowPopup(ErrorType.Error, $"Failed to read map data!");

                zipReader.Dispose();
                UpdateMapInfo(LoadedMap.Empty);
                yield break;
            }
        }
        else
        {
            Debug.LogWarning(uwr.error);
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, $"Failed to load map! {uwr.error}");

            UpdateMapInfo(LoadedMap.Empty);
            yield break;
        }
    }


    public void LoadMapZipWebGL(string directory)
    {
        if(DialogueHandler.DialogueActive)
        {
            return;
        }

        if(Loading)
        {
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "You're already loading something!");
            Debug.LogWarning("Trying to load a map while already loading!");
            return;
        }

        StartCoroutine(LoadMapZipWebGLCoroutine(directory));
        UrlArgHandler.LoadedMapURL = null;
    }
#endif


    public IEnumerator LoadMapZipURLCoroutine(string url, string mapID = null, string mapHash = null, bool noProxy = false)
    {
        Loading = true;

#if !UNITY_WEBGL || UNITY_EDITOR
        CachedFile cachedFile = FileCache.GetCachedFile(url, mapID, mapHash);
        if(!string.IsNullOrEmpty(cachedFile?.FilePath))
        {
            Debug.Log("Found map in cache.");
            LoadMapZip(cachedFile.FilePath);
            yield break;
        }
#endif

        Debug.Log($"Downloading map data from: {url}");
        LoadingMessage = "Downloading map";

        using Task<Stream> downloadTask = WebLoader.LoadFileURL(url, noProxy);
        yield return new WaitUntil(() => downloadTask.IsCompleted);

        Stream zipStream = downloadTask.Result;

        if(zipStream == null)
        {
            Debug.LogWarning("Downloaded data is null!");

            UpdateMapInfo(LoadedMap.Empty);
            yield break;
        }
#if !UNITY_WEBGL || UNITY_EDITOR
        else
        {
            FileCache.SaveFileToCache(zipStream, url, mapID, mapHash);
        }
#endif

        ZipReader zipReader = new ZipReader(null, zipStream);
        try
        {
            zipReader.Archive = new ZipArchive(zipReader.ArchiveStream, ZipArchiveMode.Read);
            StartCoroutine(LoadMapFileCoroutine(zipReader));
        }
        catch(Exception err)
        {
            zipReader.Dispose();

            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Failed to read map zip!");
            Debug.LogWarning($"Unhandled exception loading zip URL: {err.Message}, {err.StackTrace}");

            UpdateMapInfo(LoadedMap.Empty);
        }
    }


    public IEnumerator LoadMapIDCoroutine(string mapID)
    {
        Loading = true;

#if !UNITY_WEBGL || UNITY_EDITOR
        CachedFile cachedFile = FileCache.GetCachedFile(null, mapID);
        if(!string.IsNullOrEmpty(cachedFile?.FilePath))
        {
            Debug.Log("Found map in cache.");
            LoadMapZip(cachedFile.FilePath);
            yield break;
        }
#endif

        Debug.Log($"Getting BeatSaver response for ID: {mapID}");
        LoadingMessage = "Fetching map from BeatSaver";

        using Task<string> apiTask = BeatSaverHandler.GetBeatSaverMapID(mapID);
        yield return new WaitUntil(() => apiTask.IsCompleted);
        
        string mapURL = apiTask.Result;
        if(string.IsNullOrEmpty(mapURL))
        {
            Debug.Log("Empty or nonexistant URL!");
            UpdateMapInfo(LoadedMap.Empty);
            yield break;
        }

        StartCoroutine(LoadMapZipURLCoroutine(mapURL, mapID));
    }


    private IEnumerator LoadMapReplay(Replay loadedReplay)
    {
        string mapHash = loadedReplay.info.hash;
        Debug.Log($"Searching for map matching replay hash: {mapHash}");

#if !UNITY_WEBGL || UNITY_EDITOR
        CachedFile cachedFile = FileCache.GetCachedFile(null, null, mapHash);
        if(!string.IsNullOrEmpty(cachedFile?.FilePath))
        {
            Debug.Log("Found map in cache.");
            LoadMapZip(cachedFile.FilePath);
            yield break;
        }
#endif

        Debug.Log($"Getting BeatSaver response for hash: {mapHash}");
        LoadingMessage = "Fetching map from BeatSaver";

        using Task<(string, string)> apiTask = BeatSaverHandler.GetBeatSaverMapHash(mapHash);
        yield return new WaitUntil(() => apiTask.IsCompleted);

        string mapURL = apiTask.Result.Item1;
        string mapID = apiTask.Result.Item2;
        if(string.IsNullOrEmpty(mapURL))
        {
            Debug.Log("Empty or nonexistant URL! Showing manual map selection.");

            Loading = false;
            LoadingMessage = "";

            OnReplayMapPrompt?.Invoke();
            yield break;
        }

        UrlArgHandler.LoadedMapID = mapID;
        StartCoroutine(LoadMapZipURLCoroutine(mapURL, mapID, mapHash));
    }


#if !UNITY_WEBGL || UNITY_EDITOR
    private IEnumerator LoadReplayDirectoryCoroutine(string directory)
    {
        Loading = true;

        Debug.Log($"Loading replay from directory: {directory}");
        LoadingMessage = "Loading replay";

        using Task<Replay> replayTask = Task.Run(() => ReplayLoader.ReplayFromDirectory(directory));
        yield return new WaitUntil(() => replayTask.IsCompleted);

        Replay replay = replayTask.Result;
        if(replay == null)
        {
            UpdateMapInfo(LoadedMap.Empty);
            yield break;
        }

        ReplayManager.SetReplay(replay);
        StartCoroutine(LoadMapReplay(replay));
    }
#else


    private IEnumerator LoadReplayDirectoryWebGLCoroutine(string directory)
    {
        Loading = true;
        LoadingMessage = "Loading replay";

        Debug.Log("Starting web request.");
        using UnityWebRequest uwr = UnityWebRequest.Get(directory);
        yield return uwr.SendWebRequest();

        if(uwr.result == UnityWebRequest.Result.Success)
        {
            using Task<Replay> replayTask = ReplayLoader.ReplayFromStream(new MemoryStream(uwr.downloadHandler.data));
            yield return new WaitUntil(() => replayTask.IsCompleted);

            Replay replay = replayTask.Result;
            if(replay == null)
            {
                Debug.LogWarning($"Failed to read replay data!");
                ErrorHandler.Instance.ShowPopup(ErrorType.Error, $"Failed to read replay data!");

                UpdateMapInfo(LoadedMap.Empty);
                yield break;
            }

            ReplayManager.SetReplay(replay);
            StartCoroutine(LoadMapReplay(replay));
        }
        else
        {
            Debug.LogWarning(uwr.error);
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, $"Failed to load replay! {uwr.error}");

            UpdateMapInfo(LoadedMap.Empty);
            yield break;
        }
    }


    public void LoadReplayDirectoryWebGL(string directory)
    {
        if(DialogueHandler.DialogueActive)
        {
            return;
        }

        if(Loading)
        {
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "You're already loading something!");
            Debug.LogWarning("Trying to load a replay while already loading!");
            return;
        }

        StartCoroutine(LoadReplayDirectoryWebGLCoroutine(directory));
        UrlArgHandler.LoadedReplayURL = null;
    }
#endif


    private IEnumerator LoadReplayURLCoroutine(string url, bool noProxy = false)
    {
        Loading = true;

        Debug.Log($"Downloading replay file from from: {url}");
        LoadingMessage = "Downloading replay";

        using Task<Stream> downloadTask = WebLoader.LoadFileURL(url, noProxy);
        yield return new WaitUntil(() => downloadTask.IsCompleted);

        using Stream replayStream = downloadTask.Result;
        if(replayStream == null)
        {
            Debug.LogWarning("Downloaded replay is null!");

            UpdateMapInfo(LoadedMap.Empty);
            yield break;
        }

        using Task<Replay> decodeTask = ReplayLoader.ReplayFromStream(replayStream);
        yield return new WaitUntil(() => decodeTask.IsCompleted);

        Replay replay = decodeTask.Result;
        if(replay == null)
        {
            Debug.LogWarning("Failed to decode replay!");
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Failed to decode the replay!");
            UpdateMapInfo(LoadedMap.Empty);
            yield break;
        }

        ReplayManager.SetReplay(replay);
        StartCoroutine(LoadMapReplay(replay));
    }


    private IEnumerator LoadReplayIDCoroutine(string id)
    {
        Loading = true;

        Debug.Log($"Getting Beatleader response for score ID: {id}");
        LoadingMessage = "Fetching replay from Beatleader";

        using Task<string> apiTask = ReplayLoader.ReplayURLFromScoreID(id);
        yield return new WaitUntil(() => apiTask.IsCompleted);

        string replayURL = apiTask.Result;
        if(string.IsNullOrEmpty(replayURL))
        {
            Debug.Log("Empty or nonexistant URL!");
            UpdateMapInfo(LoadedMap.Empty);
            yield break;
        }

        StartCoroutine(LoadReplayURLCoroutine(replayURL));
    }


    private void UpdateMapInfo(LoadedMap newMap)
    {
        StopAllCoroutines();
        LoadingMessage = "";
        Loading = false;
        
        if(newMap.Info == null || newMap.Difficulties.Count == 0 || newMap.Song == null)
        {
            Debug.LogWarning("Failed to load map file");

            if(newMap.Song != null)
            {
#if !UNITY_WEBGL || UNITY_EDITOR
                newMap.Song.UnloadAudioData();
                Destroy(newMap.Song);
#else
                newMap.Song.Dispose();
#endif
            }
            UIStateManager.CurrentState = UIState.MapSelection;
            OnLoadingFailed?.Invoke();

            return;
        }

        UIStateManager.CurrentState = UIState.Previewer;
        
        BeatmapManager.Info = newMap.Info;
        AudioManager.Instance.MusicClip = newMap.Song;

        if(newMap.CoverImageData != null && newMap.CoverImageData.Length > 0)
        {
            CoverImageHandler.Instance.SetImageFromData(newMap.CoverImageData);
        }
        else CoverImageHandler.Instance.ClearImage();

        BeatmapManager.Difficulties = newMap.Difficulties;
        if(ReplayManager.IsReplayMode)
        {
            //Load the matching difficulty for a replay
            ReplayInfo replayInfo = ReplayManager.CurrentReplay.info;
            DifficultyCharacteristic characteristic = BeatmapInfo.CharacteristicFromString(replayInfo.mode);
            DifficultyRank difficultyRank = BeatmapInfo.DifficultyRankFromString(replayInfo.difficulty);

            List<Difficulty> difficulties = BeatmapManager.GetDifficultiesByCharacteristic(characteristic);
            if(difficulties.Count == 0 || !difficulties.Any(x => x.difficultyRank == difficultyRank))
            {
                Debug.LogWarning("Unable to find matching difficulty for replay!");
                ErrorHandler.Instance.ShowPopup(ErrorType.Warning, "Unable to find the right difficulty for this replay!");
                BeatmapManager.CurrentDifficulty = BeatmapManager.GetDefaultDifficulty();
            }
            else BeatmapManager.CurrentDifficulty = difficulties.First(x => x.difficultyRank == difficultyRank);
        }
        else BeatmapManager.CurrentDifficulty = BeatmapManager.GetDefaultDifficulty();

        OnMapLoaded?.Invoke();
    }


    public void LoadMapDirectory(string directory)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        throw new InvalidOperationException("Loading from directory doesn't work on WebGL!");
#else

        if(!File.Exists(directory))
        {
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "That file or directory doesn't exist!");
            Debug.LogWarning("Trying to load a map from a file that doesn't exist!");
            return;
        }

        if(directory.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
        {
            LoadMapZip(directory);
            HotReloader.loadedMapPath = directory;
            return;
        }

        if(directory.EndsWith(".bsor", StringComparison.InvariantCultureIgnoreCase))
        {
            StartCoroutine(LoadReplayDirectoryCoroutine(directory));
            return;
        }

        if(directory.EndsWith(".dat", StringComparison.InvariantCultureIgnoreCase))
        {
            //User is trying to load an unzipped map, get the parent directory
            DirectoryInfo parentDir = Directory.GetParent(directory);
            directory = parentDir.FullName;
        }

        FileReader fileReader = new FileReader(directory);
        StartCoroutine(LoadMapFileCoroutine(fileReader));
        HotReloader.loadedMapPath = directory;
#endif
    }


    public void LoadMapInput(string input, bool forceReplay = false)
    {
        if(DialogueHandler.DialogueActive)
        {
            Debug.LogWarning("Trying to load a map while in a dialogue!");
            return;
        }

        if(Loading)
        {
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "You're already loading something!");
            Debug.LogWarning("Trying to load a map while already loading!");
            return;
        }

        if(!ReplayManager.IsReplayMode)
        {
            HotReloader.loadedMapPath = null;
            UrlArgHandler.LoadedReplayID = null;
        }

        if(input.StartsWith("https://", StringComparison.InvariantCultureIgnoreCase) || input.StartsWith("http://", StringComparison.InvariantCultureIgnoreCase))
        {
            input = System.Web.HttpUtility.UrlDecode(input);

            if(input.Contains("beatsaver.com/maps"))
            {
                //Direct beatsaver link, should load based on ID instead
                string ID = input.Split("/").Last();
                StartCoroutine(LoadMapIDCoroutine(ID));

                UrlArgHandler.LoadedMapID = ID;
                return;
            }

            if(input.EndsWith(".zip", StringComparison.InvariantCultureIgnoreCase))
            {
                StartCoroutine(LoadMapZipURLCoroutine(input));
                UrlArgHandler.LoadedMapURL = input;
                return;
            }

            if(!ReplayManager.IsReplayMode && input.EndsWith(".bsor", StringComparison.InvariantCultureIgnoreCase))
            {
                StartCoroutine(LoadReplayURLCoroutine(input));
                UrlArgHandler.LoadedReplayURL = input;
                return;
            }

            Debug.LogWarning($"{input} doesn't link to a valid map!");
            ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Invalid URL!");
            return;
        }

        if(!ReplayManager.IsReplayMode && (forceReplay || SettingsManager.GetBool("replaymode")))
        {
            if(!input.Any(x => !char.IsDigit(x)))
            {
                StartCoroutine(LoadReplayIDCoroutine(input));
                UrlArgHandler.LoadedReplayID = input;
                return;
            }
        }
        else
        {
            const string IDchars = "0123456789abcdef";
            //If the directory doesn't contain any characters that aren't hexadecimal, that means it's probably an ID
            if(!input.Any(x => !IDchars.Contains(x)))
            {
                StartCoroutine(LoadMapIDCoroutine(input));
                UrlArgHandler.LoadedMapID = input;
                return;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        //Loading files from string directories doesn't work in WebGL
        ErrorHandler.Instance.ShowPopup(ErrorType.Error, "Invalid URL!");
#else
        UrlArgHandler.LoadedMapURL = null;
        LoadMapDirectory(input);
#endif
    }


    public void CancelMapLoading()
    {
        UpdateMapInfo(LoadedMap.Empty);
    }


    private struct ScheduledDifficulty
    {
        //Just a container for concurrent difficulty loading
        public DifficultyBeatmap Beatmap;
        public DifficultyCharacteristic Characteristic;
        public byte[] diffData;
    }
}


public class LoadedMap
{
#if !UNITY_WEBGL || UNITY_EDITOR
    public LoadedMap(LoadedMapData mapData, byte[] coverImageData, AudioClip song)
#else
    public LoadedMap(LoadedMapData mapData, byte[] coverImageData, WebAudioClip song)
#endif
    {
        MapData = mapData;
        CoverImageData = coverImageData;
        Song = song;
    }

    public LoadedMapData MapData { get; private set; }
    public BeatmapInfo Info => MapData.Info;
    public List<Difficulty> Difficulties => MapData.Difficulties;
    public byte[] CoverImageData { get; private set; }
#if !UNITY_WEBGL || UNITY_EDITOR
    public AudioClip Song { get; private set; }
#else
    public WebAudioClip Song { get; private set; }
#endif

    public static readonly LoadedMap Empty = new LoadedMap(LoadedMapData.Empty, null, null);
}


public class LoadedMapData
{
    public LoadedMapData(BeatmapInfo info, List<Difficulty> difficulties)
    {
        Info = info;
        Difficulties = difficulties;
    }

    public BeatmapInfo Info { get; private set; }
    public List<Difficulty> Difficulties { get; private set; }

    public static readonly LoadedMapData Empty = new LoadedMapData(null, new List<Difficulty>());
}


public interface IMapDataLoader : IDisposable
{
    public Task<LoadedMap> GetMap();
    public Task<LoadedMapData> GetMapData();
}


public interface IReplayLoader : IMapDataLoader
{
    public Task<Replay> GetReplay();
}
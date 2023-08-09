using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PlayerPositionManager : MonoBehaviour
{
    public static MapElementList<ReplayFrame> replayFrames = new MapElementList<ReplayFrame>();

    public static Vector3 HeadPosition { get; private set; }
    public static Quaternion HeadRotation { get; private set; }

    public static Vector3 LeftSaberTipPosition { get; private set; }
    public static Vector3 RightSaberTipPosition { get; private set; }

    [Header("Components")]
    [SerializeField] private HeadsetHandler headset;
    [SerializeField] private SaberHandler leftSaber;
    [SerializeField] private SaberHandler rightSaber;
    [SerializeField] private GameObject playerPlatform;

    [Header("Positions")]
    [SerializeField] private Vector3 defaultHmdPosition;
    [SerializeField] private Vector3 defaultLeftSaberPosition;
    [SerializeField] private Vector3 defaultRightSaberPosition;

    [Space]
    [SerializeField] private float saberTipOffset;

    [Header("Visuals")]
    [SerializeField] private Texture2D[] trailTextures;

    private string[] trailMaterialSettings = new string[]
    {
        "sabertrails",
        "sabertrailtype",
        "sabertrailbrightness"
    };

    private string[] redrawSettings = new string[]
    {
        "sabertrails",
        "sabertraillength",
        "sabertrailwidth",
        "sabertrailsegments"
    };

    private bool useTrails => SettingsManager.GetBool("sabertrails");
    private int trailIndex => Mathf.Clamp(SettingsManager.GetInt("sabertrailtype"), 0, trailTextures.Length - 1);


    public static Vector3 HeadPositionAtTime(float time)
    {
        if(replayFrames.Count == 0)
        {
            return Vector3.zero;
        }

        int lastFrameIndex = replayFrames.GetLastIndexUnoptimized(x => x.Time <= time);
        if(lastFrameIndex < 0)
        {
            //Always start with the first frame
            lastFrameIndex = 0;
        }

        return replayFrames[lastFrameIndex].headPosition;
    }


    private void SetDefaultPositions()
    {
        headset.transform.localPosition = defaultHmdPosition;
        headset.transform.localRotation = Quaternion.identity;

        leftSaber.transform.localPosition = defaultLeftSaberPosition;
        leftSaber.transform.localRotation = Quaternion.identity;

        rightSaber.transform.localPosition = defaultRightSaberPosition;
        rightSaber.transform.localRotation = Quaternion.identity;

        UpdatePositions();
    }


    private void UpdateBeat(float beat)
    {
        if(replayFrames.Count == 0)
        {
            SetDefaultPositions();
            return;
        }

        int lastFrameIndex = replayFrames.GetLastIndex(TimeManager.CurrentTime, x => x.Time <= TimeManager.CurrentTime);
        if(lastFrameIndex < 0)
        {
            //Always start with the first frame
            lastFrameIndex = 0;
        }

        //Lerp between frames to keep the visuals smooth
        ReplayFrame currentFrame = replayFrames[lastFrameIndex];
        ReplayFrame nextFrame = lastFrameIndex + 1 < replayFrames.Count ? replayFrames[lastFrameIndex + 1] : currentFrame;

        float timeDifference = nextFrame.Time - currentFrame.Time;
        float t = timeDifference <= 0 ? 0f : (TimeManager.CurrentTime - currentFrame.Time) / timeDifference;

        headset.transform.localPosition = Vector3.Lerp(currentFrame.headPosition, nextFrame.headPosition, t);
        headset.transform.localRotation = Quaternion.Lerp(currentFrame.headRotation, nextFrame.headRotation, t);

        bool leftSaberActive = leftSaber.gameObject.activeInHierarchy;
        bool rightSaberActive = rightSaber.gameObject.activeInHierarchy;

        if(leftSaberActive)
        {
            leftSaber.transform.localPosition = Vector3.Lerp(currentFrame.leftSaberPosition, nextFrame.leftSaberPosition, t);
            leftSaber.transform.localRotation = Quaternion.Lerp(currentFrame.leftSaberRotation, nextFrame.leftSaberRotation, t);
        }
        if(rightSaberActive)
        {
            rightSaber.transform.localPosition = Vector3.Lerp(currentFrame.rightSaberPosition, nextFrame.rightSaberPosition, t);
            rightSaber.transform.localRotation = Quaternion.Lerp(currentFrame.rightSaberRotation, nextFrame.rightSaberRotation, t);
        }

        if(useTrails)
        {
            if(leftSaberActive)
            {
                leftSaber.SetFrames(replayFrames, lastFrameIndex);
            }
            if(rightSaberActive)
            {
                rightSaber.SetFrames(replayFrames, lastFrameIndex);
            }
        }

        UpdatePositions();
    }


    private void UpdatePositions()
    {
        HeadPosition = headset.transform.position;
        HeadRotation = headset.transform.rotation;
        LeftSaberTipPosition = leftSaber.transform.position + (leftSaber.transform.forward * saberTipOffset);
        RightSaberTipPosition = rightSaber.transform.position + (rightSaber.transform.forward * saberTipOffset);
    }


    private void UpdateReplay(Replay newReplay)
    {
        replayFrames.Clear();

        List<Frame> frames = newReplay.frames;
        for(int i = 0; i < frames.Count; i++)
        {
            replayFrames.Add(new ReplayFrame(frames[i]));
        }

        replayFrames.SortElementsByBeat();
        UpdateSaberMaterials();
        UpdateSettings("all");
    }


    private void UpdateReplayMode(bool replayMode)
    {
        if(ReplayManager.IsReplayMode)
        {
            TimeManager.OnBeatChangedEarly += UpdateBeat;

            playerPlatform.SetActive(true);
            headset.gameObject.SetActive(true);
            leftSaber.gameObject.SetActive(true);
            rightSaber.gameObject.SetActive(true);

            UpdateReplay(ReplayManager.CurrentReplay);
        }
        else
        {
            replayFrames.Clear();

            playerPlatform.SetActive(false);
            headset.gameObject.SetActive(false);
            leftSaber.gameObject.SetActive(false);
            rightSaber.gameObject.SetActive(false);
        }
    }


    private void UpdateDifficulty(Difficulty newDifficulty)
    {
        if(!ReplayManager.IsReplayMode)
        {
            leftSaber.gameObject.SetActive(false);
            rightSaber.gameObject.SetActive(false);
            return;
        }

        if(ReplayManager.OneSaber)
        {
            leftSaber.gameObject.SetActive(ReplayManager.LeftHandedMode);
            rightSaber.gameObject.SetActive(!ReplayManager.LeftHandedMode);
        }
        else
        {
            leftSaber.gameObject.SetActive(true);
            rightSaber.gameObject.SetActive(true);
        }
    }


    public void UpdateTrailMaterials()
    {
        if(!useTrails)
        {
            return;
        }

        float brightness = SettingsManager.GetFloat("sabertrailbrightness");
        Texture2D trail = trailTextures[trailIndex];

        leftSaber.SetTrailProperties(NoteManager.RedNoteColor, brightness, trail);
        rightSaber.SetTrailProperties(NoteManager.BlueNoteColor, brightness, trail);
    }


    public void UpdateSaberMaterials()
    {
        leftSaber.SetSaberProperties(NoteManager.RedNoteColor);
        rightSaber.SetSaberProperties(NoteManager.BlueNoteColor);
    }


    public void UpdateColors(ColorPalette _)
    {
        if(!ReplayManager.IsReplayMode)
        {
            return;
        }

        UpdateSaberMaterials();
        UpdateTrailMaterials();
    }


    private void UpdateSettings(string changedSetting)
    {
        if(!ReplayManager.IsReplayMode)
        {
            return;
        }

        bool allSettings = changedSetting == "all";
        if(allSettings || trailMaterialSettings.Contains(changedSetting))
        {
            UpdateTrailMaterials();
        }
        if(allSettings || redrawSettings.Contains(changedSetting))
        {
            UpdateBeat(TimeManager.CurrentBeat);

            bool trail = useTrails; //Just to avoid an unnecessary extra settings lookup
            leftSaber.SetTrailActive(trail);
            rightSaber.SetTrailActive(trail);
        }
        if(allSettings || changedSetting == "saberwidth")
        {
            float width = SettingsManager.GetFloat("saberwidth");
            leftSaber.SetWidth(width);
            rightSaber.SetWidth(width);
        }
        if(allSettings || changedSetting == "showheadset" || changedSetting == "firstpersonreplay")
        {
            bool enableHeadset = SettingsManager.GetBool("showheadset") && !SettingsManager.GetBool("firstpersonreplay");
            headset.gameObject.SetActive(enableHeadset);
        }
        if(allSettings || changedSetting == "headsetalpha")
        {
            headset.SetAlpha(SettingsManager.GetFloat("headsetalpha"));
        }
    }


    private void OnEnable()
    {
        ReplayManager.OnReplayModeChanged += UpdateReplayMode;
        ColorManager.OnColorsChanged += UpdateColors;
        SettingsManager.OnSettingsUpdated += UpdateSettings;
        BeatmapManager.OnBeatmapDifficultyChanged += UpdateDifficulty;

        UpdateReplayMode(ReplayManager.IsReplayMode);
    }


    private void OnDisable()
    {
        TimeManager.OnBeatChangedEarly -= UpdateBeat;

        ReplayManager.OnReplayModeChanged -= UpdateReplayMode;
        ColorManager.OnColorsChanged -= UpdateColors;
        SettingsManager.OnSettingsUpdated -= UpdateSettings;
        BeatmapManager.OnBeatmapDifficultyChanged += UpdateDifficulty;

        replayFrames.Clear();
    }
}


public class ReplayFrame : MapElement
{
    public Vector3 headPosition;
    public Quaternion headRotation;

    public Vector3 leftSaberPosition;
    public Quaternion leftSaberRotation;
    
    public Vector3 rightSaberPosition;
    public Quaternion rightSaberRotation;

    public ReplayFrame(Frame f)
    {
        Time = f.time;

        headPosition = f.head.position;
        headRotation = f.head.rotation;

        leftSaberPosition = f.leftHand.position;
        leftSaberRotation = f.leftHand.rotation;

        rightSaberPosition = f.rightHand.position;
        rightSaberRotation = f.rightHand.rotation;
    }
}
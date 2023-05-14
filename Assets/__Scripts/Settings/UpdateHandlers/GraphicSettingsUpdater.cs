using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class GraphicSettingsUpdater : MonoBehaviour
{
    [SerializeField] private Volume bloomVolume;
    [SerializeField] private UniversalRenderPipelineAsset urpAsset;
    [SerializeField] private float defaultBloomStrength;

    private Bloom bloom;


    public void UpdateGraphicsSettings(string setting)
    {
        bool allSettings = setting == "all";

        if(allSettings || setting == "vsync" || setting == "framecap")
        {
            bool vsync = SettingsManager.GetBool("vsync");
            QualitySettings.vSyncCount = vsync ? 1 : 0;
            if(vsync)
            {
                Application.targetFrameRate = -1;
            }
            else
            {
                int framecap = SettingsManager.GetInt("framecap");

                //Value of -1 uncaps the framerate
                if(framecap <= 0 || framecap > 200) framecap = -1;

                Application.targetFrameRate = framecap;
            }
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        if(allSettings || setting == "antialiasing")
        {
            int antiAliasing = SettingsManager.GetInt("antialiasing");
            Camera.main.allowMSAA = antiAliasing > 0;

            switch(antiAliasing)
            {
                case <= 0:
                    urpAsset.msaaSampleCount = 0;
                    break;
                case 1:
                    urpAsset.msaaSampleCount = 2;
                    break;
                case 2:
                    urpAsset.msaaSampleCount = 4;
                    break;
                case >= 3:
                    urpAsset.msaaSampleCount = 8;
                    break;
            }
        }
#else
        if(allSettings)
        {
            Camera.main.allowMSAA = false;
        }
#endif

        if(allSettings || setting == "bloom")
        {
            bloom.intensity.value = defaultBloomStrength * SettingsManager.GetFloat("bloom");
            bloom.active = bloom.intensity.value >= 0.001f;
        }
    }


    private void Start()
    {
        bool foundBloom = bloomVolume.profile.TryGet<Bloom>(out bloom);
        if(foundBloom)
        {
            defaultBloomStrength = bloom.intensity.value;
        }
        else
        {
            Debug.LogWarning("Unable to find bloom post processing effect!");
        }

        SettingsManager.OnSettingsUpdated += UpdateGraphicsSettings;
        UpdateGraphicsSettings("all");
    }
}
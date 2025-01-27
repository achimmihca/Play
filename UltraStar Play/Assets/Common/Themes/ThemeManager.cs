using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Handles the loading, saving and application of themes for the app
// This includes the background material shader values, background particle effects, and UIToolkit colors/styles
public class ThemeManager : MonoBehaviour, ISpriteHolder
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void StaticInit()
    {
        instance = null;
    }

    /**
     * Filename without extension of the theme that should be loaded by default
     */
    public const string DefaultThemeName = "default_blue";
    private const string ThemeFolderName = "Themes";
    private const float DefaultSceneChangeAnimationTimeInSeconds = 0.25f;

    private static ThemeManager instance;
    public static ThemeManager Instance
    {
        get
        {
            if (instance == null)
            {
                ThemeManager instanceInScene = GameObjectUtils.FindComponentWithTag<ThemeManager>("ThemeManager");
                if (instanceInScene != null)
                {
                    GameObjectUtils.TryInitSingleInstanceWithDontDestroyOnLoad(ref instance, ref instanceInScene);
                }
            }
            return instance;
        }
    }

    public Material backgroundMaterial;
    public Material particleMaterial;
    public ParticleSystem backgroundParticleSystem;

    private BackgroundShaderControl backgroundShaderControl;
    public BackgroundShaderControl BackgroundShaderControl
    {
        get
        {
            if (backgroundShaderControl == null)
            {
                backgroundShaderControl = GetComponentInChildren<BackgroundShaderControl>();
            }
            return backgroundShaderControl;
        }
    }

    private Material backgroundMaterialCopy;
    private Material particleMaterialCopy;
    private RenderTexture userInterfaceRenderTexture;

    private readonly List<ThemeMeta> themeMetas = new();

    private readonly HashSet<VisualElement> alreadyProcessedVisualElements = new();

    private readonly HashSet<string> failedToLoadThemeNames = new();

    private readonly List<Sprite> loadedSprites = new();

    private bool anyThemeLoaded;

    private void Awake()
    {
        if (this != Instance)
        {
            Destroy(gameObject);
        }
    }

    private void OnEnable()
    {
        if (this != Instance)
        {
            return;
        }

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        if (this != Instance)
        {
            return;
        }

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        if (Instance == this)
        {
            ImageManager.AddSpriteHolder(this);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        if (Instance != this)
        {
            return;
        }

        if (backgroundMaterialCopy == null)
        {
            backgroundMaterialCopy = new Material(backgroundMaterial);
        }

        if (particleMaterialCopy == null)
        {
            particleMaterialCopy = new Material(particleMaterial);
        }

        // UI is rendered into a RenderTexture, which is then blended into the screen using the background shader
        if (userInterfaceRenderTexture == null)
        {
            userInterfaceRenderTexture = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
        }
        UIDocument uiDocument = GameObjectUtils.FindComponentWithTag<UIDocument>("UIDocument");
        uiDocument.panelSettings.targetTexture = userInterfaceRenderTexture;
        BackgroundShaderControl.SetUiRenderTextures(
            userInterfaceRenderTexture,
            UltraStarPlaySceneChangeAnimationControl.Instance.uiCopyRenderTexture);

        if (anyThemeLoaded)
        {
            ApplyThemeSpecificStylesToVisualElementsInScene();
        }
        else
        {
            LoadCurrentTheme();
        }
    }

    public void LoadCurrentTheme()
    {
        if (SettingsManager.Instance.Settings.DeveloperSettings.disableDynamicThemes)
        {
            return;
        }

        EScene currentScene = ESceneUtils.GetCurrentScene();
        if (currentScene == EScene.SongEditorScene)
        {
            // Song editor is out of scope for theming.
            return;
        }

        ThemeMeta themeMeta = GetCurrentTheme();
        if (themeMeta == null)
        {
            Debug.Log($"Cannot load theme. Theme is null.");
            return;
        }

        Debug.Log($"Loading theme '{themeMeta.FileNameWithoutExtension}'");
        loadedSprites.Clear();
        StartCoroutine(CoroutineUtils.ExecuteAfterDelayInFrames(0, () =>
        {
            alreadyProcessedVisualElements.Clear();
            ApplyThemeDynamicBackground(themeMeta);
            ApplyThemeSpecificStylesToVisualElementsInScene();
            anyThemeLoaded = true;
        }));
    }

    private void ApplyThemeDynamicBackground(ThemeMeta themeMeta)
    {
        DynamicBackgroundJson backgroundJson = themeMeta.ThemeJson?.dynamicBackground;
        if (backgroundJson == null)
        {
            backgroundJson = new();
        }

        // Material
        if (!backgroundJson.gradientRampFile.IsNullOrEmpty())
        {
            string gradientPath = ThemeMetaUtils.GetAbsoluteFilePath(themeMeta, backgroundJson.gradientRampFile);
            if (File.Exists(gradientPath))
            {
                TextureWrapMode textureWrapMode = backgroundJson.gradientScrollingSpeed > 0
                    ? TextureWrapMode.Repeat
                    : TextureWrapMode.Clamp;
                ImageManager.LoadSpriteFromFile(gradientPath, gradientSprite =>
                {
                    loadedSprites.Add(gradientSprite);
                    gradientSprite.texture.wrapMode = textureWrapMode;
                    backgroundMaterial.SetTexture("_ColorRampTex", gradientSprite.texture);
                });
            }
            else
            {
                Debug.LogError($"[THEME] Gradient Ramp file can't be opened at path: {backgroundJson.gradientRampFile}");
            }
        }

        if (!backgroundJson.gradientType.IsNullOrEmpty())
        {
            EDynamicBackgroundGradientType result;
            if (Enum.TryParse(backgroundJson.gradientType, true, out result))
            {
                backgroundMaterial.SetFloat("_Gradient", (int)result);
            }
        }

        Color patternColor = Color.clear; // default to clear to hide pattern if no file specified or found
        if (!backgroundJson.patternFile.IsNullOrEmpty())
        {
            string patternPath = ThemeMetaUtils.GetAbsoluteFilePath(themeMeta, backgroundJson.patternFile);
            if (File.Exists(patternPath))
            {
                ImageManager.LoadSpriteFromFile(patternPath, patternSprite =>
                {
                    loadedSprites.Add(patternSprite);
                    patternSprite.texture.wrapMode = TextureWrapMode.Repeat;
                    backgroundMaterial.SetTexture("_PatternTex", patternSprite.texture);
                });

                patternColor = backgroundJson.patternColor;
            }
            else
            {
                Debug.LogError($"[THEME] Pattern file can't be opened at path: {backgroundJson.patternFile}");
            }
        }

        float screenRatio = Screen.width / (float)Screen.height;
        backgroundMaterial.SetVector("_PatternTex_ST", new Vector4(backgroundJson.patternScale.x * screenRatio, backgroundJson.patternScale.y, backgroundJson.patternScrolling.x, backgroundJson.patternScrolling.y));
        backgroundMaterial.SetColor("_PatternColor", patternColor);
        backgroundMaterial.SetFloat("_Scale", backgroundJson.gradientScale);
        backgroundMaterial.SetFloat("_Smoothness", backgroundJson.gradientSmoothness);
        backgroundMaterial.SetFloat("_Angle", backgroundJson.gradientAngle);
        backgroundMaterial.SetFloat("_EnableGradientAnimation", backgroundJson.gradientAnimation ? 1 : 0);
        backgroundMaterial.SetFloat("_GradientAnimSpeed", backgroundJson.gradientAnimSpeed);
        backgroundMaterial.SetFloat("_GradientAnimAmp", backgroundJson.gradientAnimAmplitude);
        backgroundMaterial.SetFloat("_ColorRampScrolling", backgroundJson.gradientScrollingSpeed);
        backgroundMaterial.SetFloat("_UiShadowOpacity", backgroundJson.uiShadowOpacity);
        backgroundMaterial.SetVector("_UiShadowOffset", backgroundJson.uiShadowOffset);

        if (backgroundJson.uiShadowOpacity > 0)
        {
            backgroundMaterial.EnableKeyword("_UI_SHADOW");
        }
        else
        {
            backgroundMaterial.DisableKeyword("_UI_SHADOW");
        }

        // Particles
        if (!backgroundJson.particleFile.IsNullOrEmpty())
        {
            backgroundParticleSystem.gameObject.SetActive(true);
            string particlePath = ThemeMetaUtils.GetAbsoluteFilePath(themeMeta, backgroundJson.particleFile);
            if (File.Exists(particlePath))
            {
                ImageManager.LoadSpriteFromFile(particlePath, particleSprite =>
                {
                    loadedSprites.Add(particleSprite);
                    particleSprite.texture.wrapMode = TextureWrapMode.Clamp;
                    particleMaterial.mainTexture = particleSprite.texture;
                });
            }
            else
            {
                Debug.LogError($"[THEME] Particle file can't be opened at path: {backgroundJson.particleFile}");
            }
        }
        else
        {
            backgroundParticleSystem.gameObject.SetActive(false);
        }

        ParticleSystem.MainModule main = backgroundParticleSystem.main;
        main.startColor = new Color(1, 1, 1, backgroundJson.particleOpacity);

        backgroundParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        backgroundParticleSystem.Play();
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        // Destroy all instantiated assets
        Destroy(userInterfaceRenderTexture);

        if (backgroundMaterialCopy != null)
        {
            backgroundMaterial.CopyPropertiesFromMaterial(backgroundMaterialCopy);
            Destroy(backgroundMaterialCopy);
        }

        if (particleMaterialCopy != null)
        {
            particleMaterial.CopyPropertiesFromMaterial(particleMaterialCopy);
            Destroy(particleMaterialCopy);
        }
    }

    public void SetCurrentTheme(ThemeMeta themeMeta)
    {
        SettingsManager.Instance.Settings.GraphicSettings.themeName = themeMeta.FileNameWithoutExtension;
        LoadCurrentTheme();
    }

    public ThemeMeta GetCurrentTheme()
    {
        return GetThemeByName(SettingsManager.Instance.Settings.GraphicSettings.themeName);
    }

    public ThemeMeta GetThemeByName(string themeName)
    {
        if (failedToLoadThemeNames.Contains(themeName))
        {
            // This theme is already known to fail. Do not try again.
            return GetDefaultTheme();
        }

        ThemeMeta themeMeta = GetThemeMetas()
            .FirstOrDefault(themeMeta => themeMeta.FileNameWithoutExtension == themeName);
        if (themeMeta == null)
        {
            failedToLoadThemeNames.Add(themeName);
            Debug.LogWarning($"No theme found with name {themeName}. Using default theme instead.");
            return GetDefaultTheme();
        }

        return themeMeta;
    }

    public ThemeMeta GetDefaultTheme()
    {
        ThemeMeta defaultThemeMeta = GetThemeMetas()
            .FirstOrDefault(themeMeta => themeMeta.FileNameWithoutExtension == DefaultThemeName);

        if (defaultThemeMeta == null)
        {
            string availableThemeMetasCsv = GetThemeMetas().Select(themeMeta => themeMeta.FileNameWithoutExtension).ToCsv();
            Debug.LogError($"Default theme '{DefaultThemeName}' not found. Available themes: {availableThemeMetasCsv}");
        }

        return defaultThemeMeta;
    }

    public List<ThemeMeta> GetThemeMetas()
    {
        if (!themeMetas.IsNullOrEmpty())
        {
            return themeMetas;
        }

        List<string> themeFolders = new List<string>
        {
            $"{Application.persistentDataPath}/{ThemeFolderName}",
            ApplicationUtils.GetStreamingAssetsPath(ThemeFolderName),
        };

        themeFolders.ForEach(themeFolder =>
        {
            if (Directory.Exists(themeFolder))
            {
                string[] themeFilesInFolder = Directory.GetFiles(themeFolder, "*.json", SearchOption.AllDirectories);
                List<ThemeMeta> themeMetasInFolder = themeFilesInFolder
                    .Select(absoluteThemeFilePath => new ThemeMeta(absoluteThemeFilePath))
                    .ToList();
                themeMetas.AddRange(themeMetasInFolder);
            }
        });

        string themeNamesCsv = themeMetas.Select(themeMeta => themeMeta.FileNameWithoutExtension).ToCsv();
        Debug.Log($"Found {themeMetas.Count} themes: {themeNamesCsv}");

        return themeMetas;
    }

    public void ApplyThemeSpecificStylesToVisualElementsInScene()
    {
        if (SettingsManager.Instance.Settings.DeveloperSettings.disableDynamicThemes)
        {
            return;
        }

        ThemeMeta currentThemeMeta = GetCurrentTheme();
        if(currentThemeMeta == null
           || currentThemeMeta.ThemeJson == null)
        {
            Debug.LogWarning("Not applying theme styles because current theme is null");
            return;
        }

        UIDocument uiDocument = GameObjectUtils.FindComponentWithTag<UIDocument>("UIDocument");
        if (uiDocument == null)
        {
            return;
        }

        VisualElement root = uiDocument.rootVisualElement;

        Color backgroundButtonColor = currentThemeMeta.ThemeJson.buttonMainColor;
        Color backgroundButtonColorHover = Color.Lerp(backgroundButtonColor, Color.white, 0.2f);
        Color backgroundButtonColorFocus = Color.Lerp(backgroundButtonColor, Color.white, 0.2f);
        Color itemPickerBackgroundColor = UIUtils.ColorHSVOffset(backgroundButtonColor, 0, -0.1f, 0.01f);

        Color fontColorAll = currentThemeMeta.ThemeJson.fontColor;
        bool useGlobalFontColor = fontColorAll != Color.clear;

        Color fontColorButtons = useGlobalFontColor ? fontColorAll : currentThemeMeta.ThemeJson.fontColorButtons;
        Color fontColorLabels = useGlobalFontColor ? fontColorAll : currentThemeMeta.ThemeJson.fontColorLabels;

        // Change color of UXML elements:
        root.Query(null, "currentNoteLyrics", "previousNoteLyrics")
            .ForEach(el => el.style.color = backgroundButtonColor);

        root.Query<Button>().ForEach(button =>
        {
            foreach (string excludedNameOrClass in new []{"transparentBackgroundColor", "hiddenContinueButton"})
            {
                if (button.ClassListContains(excludedNameOrClass) || button.name == excludedNameOrClass)
                {
                    return;
                }
            }

            if (alreadyProcessedVisualElements.Contains(button))
            {
                return;
            }
            alreadyProcessedVisualElements.Add(button);

            UIUtils.SetBackgroundStyleWithHoverAndFocus(button, backgroundButtonColor, backgroundButtonColorHover, backgroundButtonColorFocus, fontColorButtons);

            VisualElement image = button.Q("image");
            if (image != null)
            {
                image.style.unityBackgroundImageTintColor = fontColorButtons;
            }

            VisualElement backImage = button.Q("backImage");
            if (backImage != null)
            {
                backImage.style.unityBackgroundImageTintColor = fontColorButtons;
            }
        });
        root.Query<VisualElement>(null, "unity-toggle__input").ForEach(entry =>
        {
            if (alreadyProcessedVisualElements.Contains(entry))
            {
                return;
            }
            alreadyProcessedVisualElements.Add(entry);
            UIUtils.SetBackgroundStyleWithHoverAndFocus(entry, entry.parent, backgroundButtonColor, backgroundButtonColorHover, backgroundButtonColorFocus, fontColorButtons);
        });
        root.Query<VisualElement>("songEntryUiRoot").ForEach(entry =>
        {
            if (alreadyProcessedVisualElements.Contains(entry))
            {
                return;
            }
            alreadyProcessedVisualElements.Add(entry);
            UIUtils.SetBackgroundStyleWithHoverAndFocus(entry, backgroundButtonColor, backgroundButtonColorHover, backgroundButtonColorFocus, fontColorButtons);
        });

        UIUtils.ApplyFontColorForElements(root, new []{"Label", "titleImage", "sceneTitle", "sceneSubtitle"}, null, fontColorLabels);
        UIUtils.ApplyFontColorForElements(root, new []{"itemLabel"}, null, fontColorButtons);

        root.Query(null, "itemPickerItemLabel").ForEach(label => label.style.backgroundColor = itemPickerBackgroundColor);
        root.Query("titleImage").ForEach(image => image.style.unityBackgroundImageTintColor = fontColorLabels);
    }

    public float GetSceneChangeAnimationTimeInSeconds()
    {
        string sceneChangeAnimationTimeString = GetCurrentTheme().ThemeJson.sceneTransitionAnimationTime;
        if (!sceneChangeAnimationTimeString.IsNullOrEmpty()
            && TimeUtils.TryParseDuration(sceneChangeAnimationTimeString, out long parsedDurationInMilliseconds))
        {
            return parsedDurationInMilliseconds / 1000f;
        }

        return DefaultSceneChangeAnimationTimeInSeconds;
    }

    public IReadOnlyCollection<Sprite> GetSprites()
    {
        return loadedSprites;
    }
}

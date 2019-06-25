﻿using alphaShot;
using BepInEx;
using BepInEx.Logging;
using BepisPlugins;
using Illusion.Game;
using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Screencap
{
    [BepInPlugin(GUID: GUID, Name: "Screenshot Manager", Version: Version)]
    public class ScreenshotManager : BaseUnityPlugin
    {
        public const string GUID = "com.bepis.bepinex.screenshotmanager";
        public const string Version = Metadata.PluginsVersion;
        private const int ScreenshotSizeMax = 4096;
        private const int ScreenshotSizeMin = 2;

        public static ScreenshotManager Instance { get; private set; }

        private readonly string screenshotDir = Path.Combine(Paths.GameRootPath, "UserData\\cap\\");
        internal AlphaShot2 currentAlphaShot;

        #region Config properties

        [Description("Capture a simple \"as you see it\" screenshot of the game.\n" +
                     "Not affected by settings for rendered screenshots.")]
        public static SavedKeyboardShortcut CK_Capture { get; private set; }

        [Description("Capture a high quality screenshot without UI.")]
        public static SavedKeyboardShortcut CK_CaptureAlpha { get; private set; }

        [Description("Captures a 360 screenshot around current camera. The created image is in equirectangular " +
                     "format and can be viewed by most 360 image viewers (e.g. Google Cardboard). They can be uploaded to flickr.")]
        public static SavedKeyboardShortcut CK_Capture360 { get; private set; }

        [Description("Capture a high quality screenshot without UI in stereoscopic 3D (2 captures for each eye in one image).\n\n" +
                     "These images can be viewed by crossing your eyes or any stereoscopic image viewer.")]
        public static SavedKeyboardShortcut CK_CaptureAlphaIn3D { get; private set; }

        [Description("Captures a 360 screenshot around current camera in stereoscopic 3D (2 captures for each eye in one image).\n\n" +
                     "These images can be viewed by image viewers supporting 3D stereo format (e.g. VR Media Player - 360° Viewer).")]
        public static SavedKeyboardShortcut CK_Capture360in3D { get; private set; }

        public static SavedKeyboardShortcut CK_Gui { get; private set; }

        [Category("Rendered screenshot output resolution")]
        [DisplayName("Horizontal (Width in px)")]
        [AcceptableValueRange(ScreenshotSizeMin, ScreenshotSizeMax, false)]
        public static ConfigWrapper<int> ResolutionX { get; private set; }

        [Category("Rendered screenshot output resolution")]
        [DisplayName("Vertical (Height in px)")]
        [AcceptableValueRange(ScreenshotSizeMin, ScreenshotSizeMax, false)]
        public static ConfigWrapper<int> ResolutionY { get; private set; }

        [DisplayName("360 screenshot resolution")]
        [Description("Horizontal resolution of 360 screenshots. Decrease if you have issues.\n\n" +
                     "WARNING: Memory usage can get VERY high - 4096 needs around 4GB of free RAM/VRAM to take, 8192 will need much more.")]
        [AcceptableValueList(new object[] { 1024, 2048, 4096, 8192 })]
        public static ConfigWrapper<int> Resolution360 { get; private set; }

        [DisplayName("3D screenshot eye separation")]
        [Description("Distance between the two captured stereoscopic screenshots in arbitrary units.")]
        [AcceptableValueRange(0.01f, 0.5f, false)]
        public static ConfigWrapper<float> EyeSeparation { get; private set; }

        [DisplayName("3D screenshot image separation offset")]
        [Description("Move images in stereoscopic screenshots closer together by this percentage (discards overlapping parts). " +
                     "Useful for viewing with crossed eyes. Does not affect 360 stereoscopic screenshots.")]
        [AcceptableValueRange(0f, 1f)]
        public static ConfigWrapper<float> ImageSeparationOffset { get; private set; }

        [DisplayName("Rendered screenshot upsampling ratio")]
        [Description("Capture screenshots in a higher resolution and then downscale them to desired size. " +
                     "Prevents aliasing, perserves small details and gives a smoother result, but takes longer to create.")]
        [AcceptableValueRange(1, 4, false)]
        public static ConfigWrapper<int> DownscalingRate { get; private set; }

        [DisplayName("Card image upsampling ratio")]
        [Description("Capture character card images in a higher resolution and then downscale them to desired size. " +
                     "Prevents aliasing, perserves small details and gives a smoother result, but takes longer to create.")]
        [AcceptableValueRange(1, 4, false)]
        public static ConfigWrapper<int> CardDownscalingRate { get; private set; }

        [DisplayName("Transparency in rendered screenshots")]
        [Description("Replaces background with transparency in rendered image. Works only if there are no 3D objects covering the " +
                     "background (e.g. the map). Works well in character creator and studio.")]
        public static ConfigWrapper<bool> CaptureAlpha { get; private set; }

        [DisplayName("Show messages on screen")]
        [Description("Whether screenshot messages will be displayed on screen. Messages will still be written to the log.")]
        public static ConfigWrapper<bool> ScreenshotMessage { get; private set; }

        #endregion

        private string GetUniqueFilename()
        {
            return Path.GetFullPath(Path.Combine(screenshotDir, $"Koikatsu-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.png"));
        }

        protected void Awake()
        {
            if (Instance)
            {
                DestroyImmediate(this);
                return;
            }
            Instance = this;
            CK_Capture = new SavedKeyboardShortcut("Take UI screenshot", this, new KeyboardShortcut(KeyCode.F9));
            CK_CaptureAlpha = new SavedKeyboardShortcut("Take rendered screenshot", this, new KeyboardShortcut(KeyCode.F11));
            CK_Capture360 = new SavedKeyboardShortcut("Take 360 screenshot", this, new KeyboardShortcut(KeyCode.F11, KeyCode.LeftControl));
            CK_CaptureAlphaIn3D = new SavedKeyboardShortcut("Take rendered 3D screenshot", this, new KeyboardShortcut(KeyCode.F11, KeyCode.LeftAlt));
            CK_Capture360in3D = new SavedKeyboardShortcut("Take 360 3D screenshot", this, new KeyboardShortcut(KeyCode.F11, KeyCode.LeftControl, KeyCode.LeftShift));
            CK_Gui = new SavedKeyboardShortcut("Open settings window", this, new KeyboardShortcut(KeyCode.F11, KeyCode.LeftShift));

            ResolutionX = new ConfigWrapper<int>("resolution-x", this, Screen.width);
            ResolutionY = new ConfigWrapper<int>("resolution-y", this, Screen.height);
            Resolution360 = new ConfigWrapper<int>("resolution-360", this, 4096);
            EyeSeparation = new ConfigWrapper<float>("3d-eye-separation", this, 0.18f);
            ImageSeparationOffset = new ConfigWrapper<float>("3d-image-stitching-offset", this, 0.21f);

            ResolutionX.SettingChanged += (sender, args) => ResolutionXBuffer = ResolutionX.Value.ToString();
            ResolutionY.SettingChanged += (sender, args) => ResolutionYBuffer = ResolutionY.Value.ToString();

            DownscalingRate = new ConfigWrapper<int>("downscalerate", this, 2);
            CardDownscalingRate = new ConfigWrapper<int>("carddownscalerate", this, 3);
            CaptureAlpha = new ConfigWrapper<bool>("capturealpha", this, true);
            ScreenshotMessage = new ConfigWrapper<bool>("screenshotmessage", this, true);

            SceneManager.sceneLoaded += (s, a) => InstallSceenshotHandler();
            InstallSceenshotHandler();

            if (!Directory.Exists(screenshotDir))
                Directory.CreateDirectory(screenshotDir);

            Hooks.InstallHooks();

            I360Render.Init();
        }

        private void InstallSceenshotHandler()
        {
            if (!Camera.main || !Camera.main.gameObject) return;
            currentAlphaShot = Camera.main.gameObject.GetOrAddComponent<AlphaShot2>();
        }

        protected void Update()
        {
            if (CK_Gui.IsDown())
            {
                uiShow = !uiShow;
                ResolutionXBuffer = ResolutionX.Value.ToString();
                ResolutionYBuffer = ResolutionY.Value.ToString();
            }
            else if (CK_CaptureAlpha.IsDown()) StartCoroutine(TakeCharScreenshot(false));
            else if (CK_Capture.IsDown()) TakeScreenshot();
            else if (CK_Capture360.IsDown()) StartCoroutine(Take360Screenshot(false));
            else if (CK_CaptureAlphaIn3D.IsDown()) StartCoroutine(TakeCharScreenshot(true));
            else if (CK_Capture360in3D.IsDown()) StartCoroutine(Take360Screenshot(true));
        }

        private void TakeScreenshot()
        {
            var filename = GetUniqueFilename();
            Application.CaptureScreenshot(filename);

            StartCoroutine(TakeScreenshotLog(filename));
        }

        private IEnumerator TakeScreenshotLog(string filename)
        {
            yield return new WaitForEndOfFrame();
            Utils.Sound.Play(SystemSE.photo);
            BepInEx.Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"UI screenshot saved to {filename}");
        }

        private IEnumerator TakeCharScreenshot(bool in3D)
        {
            if (currentAlphaShot != null)
            {
                var filename = GetUniqueFilename();

                if (!in3D)
                {
                    yield return new WaitForEndOfFrame();
                    var capture = currentAlphaShot.CaptureTex(ResolutionX.Value, ResolutionY.Value, DownscalingRate.Value, CaptureAlpha.Value);
                    File.WriteAllBytes(filename, capture.EncodeToPNG());
                    Destroy(capture);
                }
                else
                {
                    var targetTr = Camera.main.transform;
                    // Needed for studio because it prevents changes to position
                    var cc = targetTr.GetComponent<Studio.CameraControl>();
                    if (cc != null) cc.enabled = false;
                    Time.timeScale = 0.01f;
                    yield return new WaitForEndOfFrame();

                    targetTr.localPosition += targetTr.right * EyeSeparation.Value / 2;
                    yield return new WaitForEndOfFrame();
                    var capture = currentAlphaShot.CaptureTex(ResolutionX.Value, ResolutionY.Value, DownscalingRate.Value, CaptureAlpha.Value);

                    targetTr.localPosition -= targetTr.right * EyeSeparation.Value;
                    yield return new WaitForEndOfFrame();
                    var capture2 = currentAlphaShot.CaptureTex(ResolutionX.Value, ResolutionY.Value, DownscalingRate.Value, CaptureAlpha.Value);

                    targetTr.localPosition += targetTr.right * EyeSeparation.Value / 2;

                    if (cc != null) cc.enabled = true;
                    Time.timeScale = 1;

                    // Merge the two images together
                    var xAdjust = (int)(capture.width * ImageSeparationOffset.Value);
                    var result = new Texture2D((capture.width - xAdjust) * 2, capture.height, TextureFormat.ARGB32, false);
                    for (int x = 0; x < result.width; x++)
                    {
                        var first = x < result.width / 2;
                        var targetX = first ? x : x - capture.width + xAdjust * 2;
                        var targetTex = first ? capture : capture2;
                        for (int y = 0; y < result.height; y++)
                        {
                            result.SetPixel(x, y, targetTex.GetPixel(targetX, y));
                        }
                    }
                    result.Apply();

                    File.WriteAllBytes(filename, result.EncodeToPNG());

                    Destroy(capture);
                    Destroy(capture2);
                    Destroy(result);
                }

                Utils.Sound.Play(SystemSE.photo);
                BepInEx.Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"Character screenshot saved to {filename}");
            }
            else
            {
                BepInEx.Logger.Log(LogLevel.Message, "Can't render a screenshot here, try UI screenshot instead");
            }
        }

        private IEnumerator Take360Screenshot(bool in3D)
        {
            yield return new WaitForEndOfFrame();

            var filename = GetUniqueFilename();
            if (!in3D)
            {
                yield return new WaitForEndOfFrame();
                File.WriteAllBytes(filename, I360Render.Capture(Resolution360.Value, false));
            }
            else
            {
                var targetTr = Camera.main.transform;

                // Needed for studio because it prevents changes to position
                var cc = targetTr.GetComponent<Studio.CameraControl>();
                if (cc != null) cc.enabled = false;
                Time.timeScale = 0.01f;
                yield return new WaitForEndOfFrame();

                targetTr.localPosition += targetTr.right * EyeSeparation.Value / 2;
                // Let the game render at the new position
                yield return new WaitForEndOfFrame();
                var capture = I360Render.CaptureTex(Resolution360.Value);

                targetTr.localPosition -= targetTr.right * EyeSeparation.Value;
                yield return new WaitForEndOfFrame();
                var capture2 = I360Render.CaptureTex(Resolution360.Value);

                targetTr.localPosition += targetTr.right * EyeSeparation.Value / 2;

                if (cc != null) cc.enabled = true;
                Time.timeScale = 1;

                var result = new Texture2D(capture.width * 2, capture.height, TextureFormat.ARGB32, false);
                result.SetPixels32(0, 0, capture.width, capture.height, capture.GetPixels32());
                result.SetPixels32(capture.width, 0, capture2.width, capture2.height, capture2.GetPixels32());
                result.Apply();

                File.WriteAllBytes(filename, I360Render.InsertXMPIntoTexture2D_PNG(result));
                Destroy(result);
                Destroy(capture);
                Destroy(capture2);
            }

            Utils.Sound.Play(SystemSE.photo);
            BepInEx.Logger.Log(ScreenshotMessage.Value ? LogLevel.Message : LogLevel.Info, $"360 screenshot saved to {filename}");
        }

        #region UI
        private readonly int uiWindowHash = GUID.GetHashCode();
        private Rect uiRect = new Rect(20, Screen.height / 2 - 150, 160, 223);
        private bool uiShow = false;
        private string ResolutionXBuffer = "", ResolutionYBuffer = "";

        protected void OnGUI()
        {
            if (uiShow)
                uiRect = GUILayout.Window(uiWindowHash, uiRect, WindowFunction, "Screenshot settings");
        }

        private void WindowFunction(int windowID)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            {
                GUILayout.Label("Output resolution (W/H)", new GUIStyle
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = new GUIStyleState
                    {
                        textColor = Color.white
                    }
                });

                GUILayout.BeginHorizontal();
                {
                    GUI.SetNextControlName("X");
                    ResolutionXBuffer = GUILayout.TextField(ResolutionXBuffer);

                    GUILayout.Label("x", new GUIStyle
                    {
                        alignment = TextAnchor.LowerCenter,
                        normal = new GUIStyleState
                        {
                            textColor = Color.white
                        }
                    }, GUILayout.ExpandWidth(false));

                    GUI.SetNextControlName("Y");
                    ResolutionYBuffer = GUILayout.TextField(ResolutionYBuffer);

                    var focused = GUI.GetNameOfFocusedControl();
                    if (focused != "X" && focused != "Y")
                    {
                        if (!int.TryParse(ResolutionXBuffer, out int x))
                            x = ResolutionX.Value;
                        if (!int.TryParse(ResolutionYBuffer, out int y))
                            y = ResolutionY.Value;
                        ResolutionXBuffer = (ResolutionX.Value = Mathf.Clamp(x, ScreenshotSizeMin, ScreenshotSizeMax)).ToString();
                        ResolutionYBuffer = (ResolutionY.Value = Mathf.Clamp(y, ScreenshotSizeMin, ScreenshotSizeMax)).ToString();
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.Space(2);

                    if (GUILayout.Button("Set to screen size"))
                    {
                        ResolutionX.Value = Screen.width;
                        ResolutionY.Value = Screen.height;
                    }

                    if (GUILayout.Button("Rotate 90 degrees"))
                    {
                        var curerntX = ResolutionX.Value;
                        ResolutionX.Value = ResolutionY.Value;
                        ResolutionY.Value = curerntX;
                    }
                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUI.skin.box);
                {
                    GUILayout.Label("Screen upsampling rate", new GUIStyle
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = new GUIStyleState
                        {
                            textColor = Color.white
                        }
                    });

                    GUILayout.BeginHorizontal();
                    {
                        int downscale = (int)Math.Round(GUILayout.HorizontalSlider(DownscalingRate.Value, 1, 4));

                        GUILayout.Label($"{downscale}x", new GUIStyle
                        {
                            alignment = TextAnchor.UpperRight,
                            normal = new GUIStyleState
                            {
                                textColor = Color.white
                            }
                        }, GUILayout.ExpandWidth(false));
                        DownscalingRate.Value = downscale;
                    }
                    GUILayout.EndHorizontal();

                }
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUI.skin.box);
                {
                    GUILayout.Label("Card upsampling rate", new GUIStyle
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = new GUIStyleState
                        {
                            textColor = Color.white
                        }
                    });

                    GUILayout.BeginHorizontal();
                    {
                        int carddownscale = (int)Math.Round(GUILayout.HorizontalSlider(CardDownscalingRate.Value, 1, 4));

                        GUILayout.Label($"{carddownscale}x", new GUIStyle
                        {
                            alignment = TextAnchor.UpperRight,
                            normal = new GUIStyleState
                            {
                                textColor = Color.white
                            }
                        }, GUILayout.ExpandWidth(false));
                        CardDownscalingRate.Value = carddownscale;
                    }
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();

                CaptureAlpha.Value = GUILayout.Toggle(CaptureAlpha.Value, "Transparent background");

                if (GUILayout.Button("Open screenshot dir"))
                    Process.Start(screenshotDir);

                GUILayout.Space(3);
                GUILayout.Label("More in Plugin Settings");

                GUI.DragWindow();
            }
            #endregion
        }
    }
}

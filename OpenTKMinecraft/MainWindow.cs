﻿#define SYNCRONIZED_RENDER_AND_UPDATE

using System.Windows.Media.Imaging;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;
using System.Drawing;
using System.Linq;
using System.IO;
using System;

using OpenTK.Graphics.OpenGL4;
using OpenTK.Graphics;
using OpenTK.Input;
using OpenTK;

using OpenTKMinecraft.Components.UI;
using OpenTKMinecraft.Components;
using OpenTKMinecraft.Minecraft;

using static System.Math;

using WM = System.Windows.Media;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenTKMinecraft
{
    public sealed unsafe class MainWindow
        : GameWindow
    {
        internal const int KEYBOARD_TOGGLE_DELAY = 200;

        public World World => Scene.Object.World;
        public HUDWindow PauseScreen => HUD.PauseScreen as HUDWindow;
        public PlayerCamera Camera => Scene.Object.Camera as PlayerCamera;
        public PostEffectShaderProgram<Scene> Scene { private set; get; }
        public HUD HUD { private set; get; }

        public float MouseSensitivityFactor { set; get; } = 1;
        public bool IsPaused { private set; get; }
        public double PausedTime { private set; get; }
        public double Time { private set; get; }
        public string[] Arguments { get; }

        private readonly Queue<Action> _queue = new Queue<Action>();
        private int _mousex, _mousey;
        private float _mousescroll;


        public MainWindow(string[] args)
            : base(1920, 1080, new GraphicsMode(new ColorFormat(32, 32, 32, 32), 32, 0, 4), nameof(MainWindow), GameWindowFlags.Default, DisplayDevice.Default, MainProgram.GL_VERSION_MAJ, MainProgram.GL_VERSION_MIN, GraphicsContextFlags.ForwardCompatible)
        {
            Arguments = args;
            MouseSensitivityFactor = 2;
            WindowBorder = WindowBorder.Resizable;
            WindowState = WindowState.Normal;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        private string GetActiveWindowTitle()
        {
            const int nChars = 256;
            StringBuilder Buff = new StringBuilder(nChars);
            IntPtr handle = GetForegroundWindow();

            if (GetWindowText(handle, Buff, nChars) > 0)
            {
                return Buff.ToString();
            }
            return null;
        }


        protected override void OnLoad(EventArgs e)
        {
            MainProgram.spscreen.Text = ("Loading Shaders ...", "");

            Closed += (s, a) => Exit();

            Scene = new PostEffectShaderProgram<Scene>(
                new Scene(this, new ShaderProgram(
                    "Scene Shader",
                    new[] { "SCENE" },
                    (ShaderProgramType.VertexShader, "shaders/scene.vert"),
                    (ShaderProgramType.FragmentShader, "shaders/scene.frag")
                ))
                {
                    Camera = new PlayerCamera
                    {
                        IsStereoscopic = false,
                    },
                },
                new ShaderProgram(
                    "Scene Effect",
                    new[] { "EFFECT" },
                    (ShaderProgramType.VertexShader, "shaders/scene_effect.vert"),
                    (ShaderProgramType.FragmentShader, "shaders/scene_effect.frag")
                ),
                this
            )
            {
                //UsePostEffect = false
            };
            HUD = new HUD(this, new ShaderProgram(
                "HUD Shader",
                new[] { "HUD" },
                (ShaderProgramType.VertexShader, "shaders/hud.vert"),
                (ShaderProgramType.FragmentShader, "shaders/hud.frag")
            ));

            BuildHUD();

            MainProgram.spscreen.Text = ("Intializing textures ...", "");
            TextureSet.InitKnownMaterialTexures(Scene.Object.Program);

            Scene.Object.AddLight(Light.CreateDirectionalLight(new Vector3(-1, -1, 0), Color.WhiteSmoke));
            Scene.Object.AddLight(Light.CreatePointLight(new Vector3(0, 0, 2), Color.Wheat, 10));

            BuildScene();
            ResetCamera();

            CursorVisible = false;
            VSync = VSyncMode.Off;
            //WindowState = WindowState.Maximized;

            MainProgram.spscreen.Text = ("Finished.", "");
            Thread.Sleep(500);
            MainProgram.spscreen.Close();

            ShowHelp();
        }

        internal void BuildHUD()
        {
            HUD.PauseScreen = new HUDWindow(null)
            {
                Width = 500,
                Height = 1080,
                Padding = 10,
                Text = "PAUSE MENU",
                Font = new Font("Purista", 40, FontStyle.Bold | FontStyle.Underline, GraphicsUnit.Point),
                ForegroundColor = Color.DarkRed
            };

            Font fnt = new Font("Purista", 24, GraphicsUnit.Point);
            Color fg = Color.Black;
            Color bg = Color.Gray;
            float hgt = 40;

            PauseScreen.AddFill(new HUDCheckbox(null)
            {
                Font = fnt,
                Height = hgt,
                Text = "Vertical Synchronization",
                BackgroundColor = bg,
                ForegroundColor = fg,
                BeforeRender = c => Invoke(() => (c as HUDCheckbox).IsChecked = VSync != VSyncMode.Off),
            }, 110).StateChanged += (_, a) => Invoke(() => VSync = a ? VSyncMode.On : VSyncMode.Off);
            PauseScreen.AddFill(new HUDCheckbox(null)
            {
                Font = fnt,
                Height = hgt,
                Text = "Use post-render effects",
                BackgroundColor = bg,
                ForegroundColor = fg,
                BeforeRender = c => (c as HUDCheckbox).IsChecked = Scene.UsePostEffect,
            }, 160).StateChanged += (_, a) => Invoke(() =>
            {
                if (Scene.UsePostEffect = a)
                    Camera.IsStereoscopic = false;
            });
            PauseScreen.AddFill(new HUDCheckbox(null)
            {
                Font = fnt,
                Height = hgt,
                Text = "Use Head-Up-Display (HUD)",
                BackgroundColor = bg,
                ForegroundColor = fg,
                BeforeRender = c => (c as HUDCheckbox).IsChecked = HUD.UseHUD,
            }, 210).StateChanged += (_, __) => HUD.UseHUD ^= true;
            PauseScreen.AddFill(new HUDCheckbox(null)
            {
                Font = fnt,
                Height = hgt,
                Text = "Use stereoscopic camera",
                BackgroundColor = bg,
                ForegroundColor = fg,
                BeforeRender = c => (c as HUDCheckbox).IsChecked = Camera.IsStereoscopic,
            }, 260).StateChanged += (_, __) =>
            {
                if (Camera.IsStereoscopic ^= true)
                    Scene.UsePostEffect = false;
            };

            PauseScreen.AddFill(new HUDOptionbox(null)
            {
                Font = fnt,
                Height = hgt * 3,
                BackgroundColor = bg,
                ForegroundColor = fg,
                Options = new[] { "Render points", "Render Lines", "Render faces" },
                BeforeRender = c =>
                {
                    var o = c as HUDOptionbox;

                    switch (Scene.Object.Program.PolygonMode)
                    {
                        case PolygonMode.Point:
                            o.SelectedIndex = 0;
                            break;
                        case PolygonMode.Line:
                            o.SelectedIndex = 1;
                            break;
                        case PolygonMode.Fill:
                            o.SelectedIndex = 2;
                            break;
                    }
                }
            }, 380).SelectedIndexChanged += (_, a) => Scene.Object.Program.PolygonMode = a == 0 ? PolygonMode.Point : a == 1 ? PolygonMode.Line : PolygonMode.Fill;
            PauseScreen.AddFill(new HUDButton(null)
            {
                Font = fnt,
                Height = hgt,
                Text = "Continue",
                BackgroundColor = bg,
                ForegroundColor = fg,
            }, 500).Clicked += (s, a) =>
            {
                int x = X + (Width / 2);
                int y = Y + (Height / 2);

                IsPaused = false;
                System.Windows.Forms.Cursor.Position = new Point(x, y);

                _mousex = x;
                _mousey = y;
                _mousescroll = Mouse.GetState().WheelPrecise;
            };
            PauseScreen.AddFill(new HUDButton(null)
            {
                Font = fnt,
                Height = hgt,
                Text = "Help",
                BackgroundColor = bg,
                ForegroundColor = fg,
            }, 550).Clicked += (s, a) => ShowHelp();
            PauseScreen.AddFill(new HUDButton(null)
            {
                Font = fnt,
                Height = hgt,
                Text = "Exit",
                BackgroundColor = bg,
                ForegroundColor = fg,
            }, 600).Clicked += (s, a) => Exit();
        }

        internal void BuildScene()
        {
            MainProgram.spscreen.Text = ("Loading World ...", "Building scene ...");

            World.Clear();
            World[0, 15, 0].Material = BlockMaterial.__DEBUG__;
            World[10, 4, 10].Material = BlockMaterial.Glowstone;

            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                    if ((i == 0) || (i == 3) || (j == 0) || (j == 3))
                        World[1 - i, j + 1, 0].Material = ((i ^ j) & 1) != 0 ? BlockMaterial.Stone : BlockMaterial.Diamond;

            int side = 10;

            for (int i = -side; i <= side; ++i)
                for (int j = -side; j <= side; ++j)
                {
                    int y = (int)(Sin((i + Sin(i) / 3 - j) / 3) * 1.5);

                    if ((i * i + j * j) < 15)
                    {
                        //World[i, y + 10, j].Material = BlockMaterial.Sand;
                        World[i, y - 1, j].Material = BlockMaterial.Grass;
                    }
                    else
                        World[i, y, j].Material = BlockMaterial.Grass;
                }

            (int xp, int yp) = (15, 15);

            // Scene.World[xp, 3, yp].Material = BlockMaterial.Glowstone;

            for (int i = -2; i <= 2; ++i)
                for (int j = -2; j <= 2; ++j)
                    if ((i >= -1) && (i < 2) && (j >= -1) && (j < 2))
                    {
                        World[xp + i, 3, yp + j].Material = BlockMaterial.Stone;
                        World[xp + i, 4, yp + j].Material = BlockMaterial.Water;
                        World[xp + i, 4, yp + j].Move(0, -.15f, 0);
                    }
                    else
                        World[xp + i, 4, yp + j].Material = BlockMaterial.Stone;

            // Scene.World.PlaceCustomBlock(4, 1, 0, WavefrontFile.FromPath("resources/center-piece.obj"));
        }

        private void ResetCamera()
        {
            Camera.MoveTo(new Vector3(0, 6, -8));
            Camera.ResetZoom();
            Camera.HorizontalAngle = (float)(PI / 2);
            Camera.VerticalAngle = -.25f;
            Camera.EyeSeparation = .1f;
            Camera.FocalDistance = 10f;
        }

        public override void Exit()
        {
            Scene.Dispose();
            HUD.Dispose();

            ShaderProgram.DisposeAll();

            base.Exit();
        }

        protected override void OnResize(EventArgs e)
        {
            GL.Viewport(0, 0, Width, Height);

            Scene.OnWindowResize(this, e);

            const float BORDER = 75;

            PauseScreen.Width = Min(500, Width - (2 * BORDER));
            PauseScreen.Height = Height - (2 * BORDER);
            PauseScreen.CenterX = Width / 2f;
            PauseScreen.CenterY = Height / 2f;
        }

#if SYNCRONIZED_RENDER_AND_UPDATE
        private new void OnUpdateFrame(FrameEventArgs e)
#else
        protected override void OnUpdateFrame(FrameEventArgs e)
#endif
        {
            HandleInput(e.Time);

            if (IsPaused)
                PausedTime += e.Time;
            else
                Time += e.Time;

            HUD.Update(Time + PausedTime, e.Time);

            if (IsPaused)
                return;

            //TODO: Comment this out
            Scene.Object.Lights[1].Position = Matrix3.CreateRotationY((float)Time) * new Vector3(0, 2, 4);
            Scene.Update(Time, e.Time, (float)Width / Height);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            Scene.Render(Time, Width, Height);
            HUD.Render(Time + PausedTime, Width, Height);

            SwapBuffers();

            lock (_queue)
                foreach (Action a in _queue)
                    a();

#if SYNCRONIZED_RENDER_AND_UPDATE
            OnUpdateFrame(e);
#endif
        }

       
        internal void HandleInput(double delta)
        {
            if (GetActiveWindowTitle() != "OpenTK Minecraft") return;

            KeyboardState kstate = Keyboard.GetState();
            MouseState mstate = Mouse.GetState();
            int δx = _mousex - mstate.X;
            int δy = _mousey - mstate.Y;
            float δs = _mousescroll - mstate.WheelPrecise;
            float speed = 15 * (float)delta; // == 15 m/s

            if (kstate.IsKeyDown(Key.H))
            {
                ShowHelp();

                return;
            }
            if (kstate.IsKeyDown(Key.Number4))
            {
                if (Camera.IsStereoscopic ^= true)
                    Scene.UsePostEffect = false;

                Thread.Sleep(KEYBOARD_TOGGLE_DELAY);
            }
            if (kstate.IsKeyDown(Key.Number5))
            {
                if (Scene.UsePostEffect ^= true)
                    Camera.IsStereoscopic = false;

                Thread.Sleep(KEYBOARD_TOGGLE_DELAY);
            }
            if (kstate.IsKeyDown(Key.Number6))
            {
                HUD.UseHUD ^= true;

                Thread.Sleep(KEYBOARD_TOGGLE_DELAY);
            }
            if (kstate.IsKeyDown(Key.X))
            {
                Scene.Effect++;
                Scene.Effect = (PredefinedShaderEffect)((int)Scene.Effect % ((Enum.GetValues(typeof(PredefinedShaderEffect)) as int[]).Max() + 1));

                Thread.Sleep(KEYBOARD_TOGGLE_DELAY);
            }
            if (kstate.IsKeyDown(Key.Escape))
            {
                if (kstate.IsKeyDown(Key.LShift) || kstate.IsKeyDown(Key.RShift))
                    Exit();

                IsPaused ^= true;

                Thread.Sleep(KEYBOARD_TOGGLE_DELAY);

                /*
                  	if (!IsPaused)
                {
                    _mousex = mstate.X;
                    _mousey = mstate.Y;
                    _mousescroll = mstate.WheelPrecise;
                }
*/

                return;
            }

            if (IsPaused)
                return;

            if (kstate.IsKeyDown(Key.ShiftLeft))
                speed /= 10;
            if (kstate.IsKeyDown(Key.C))
            {
                speed *= 3;

                Scene.EdgeBlurMode = EdgeBlurMode.RadialBlur;
            }
            else
                Scene.EdgeBlurMode = EdgeBlurMode.BoxBlur;

            if (kstate.IsKeyDown(Key.U))
            {
                BuildScene();

                Thread.Sleep(KEYBOARD_TOGGLE_DELAY);
            }
            if (kstate.IsKeyDown(Key.W))
                Camera.MoveForwards(speed);
            if (kstate.IsKeyDown(Key.S))
                Camera.MoveBackwards(speed);
            if (kstate.IsKeyDown(Key.A))
                Camera.MoveLeft(speed);
            if (kstate.IsKeyDown(Key.D))
                Camera.MoveRight(speed);
            if (kstate.IsKeyDown(Key.Space))
                Camera.MoveUp(speed);
            if (kstate.IsKeyDown(Key.ControlLeft))
                Camera.MoveDown(speed);
            if (kstate.IsKeyDown(Key.Q))
                --Camera.ZoomFactor;
            if (kstate.IsKeyDown(Key.E))
                ++Camera.ZoomFactor;
            if (kstate.IsKeyDown(Key.R))
                ResetCamera();
            if (kstate.IsKeyDown(Key.F))
            {
                const string dir = "screenshots";

                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string path = $"{dir}/Screenshot-{DateTime.Now:yyyy-MM-dd-HH-mm-ss-ffffff}";

                if (kstate.IsKeyDown(Key.LShift) || kstate.IsKeyDown(Key.RShift))
                {
                    float[] hdr = new float[Width * Height * 4];
                    const double γ = 2.2;

                    fixed (float* ptr = hdr)
                        GL.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.Float, (IntPtr)ptr);

                    // convert gamma-corrected to linear
                    Parallel.For(0, hdr.Length, i => hdr[i] = (float)Pow(hdr[i], γ));

                    using (FileStream fs = new FileStream($"{path}.tif", FileMode.Create))
                    {
                        TiffBitmapEncoder enc = new TiffBitmapEncoder();

                        enc.Frames.Add(
                            BitmapFrame.Create(
                                new TransformedBitmap(
                                    BitmapSource.Create(
                                        Width,
                                        Height,
                                        96,
                                        96,
                                        WM.PixelFormats.Rgba128Float,
                                        BitmapPalettes.WebPalette,
                                        hdr,
                                        Width * 16
                                    ),
                                    new WM.ScaleTransform(1, -1, 0.5, 0.5)
                                )
                            )
                        );
                        enc.Save(fs);
                    }
                }
                else
                    using (Bitmap bmp = new Bitmap(Width, Height))
                    {
                        System.Drawing.Imaging.BitmapData data = bmp.LockBits(new Rectangle(0, 0, Width, Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

                        GL.ReadPixels(0, 0, Width, Height, PixelFormat.Bgr, PixelType.UnsignedByte, data.Scan0);

                        bmp.UnlockBits(data);
                        bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
                        bmp.Save($"{path}.png");

                        Thread.Sleep(KEYBOARD_TOGGLE_DELAY);
                    }
            }

            if (Camera.IsStereoscopic)
            {
                Camera.FocalDistance *= (float)Pow(1.3, δs);

                if (kstate.IsKeyDown(Key.PageUp))
                    Camera.FocalDistance *= 1.1f;
                if (kstate.IsKeyDown(Key.PageDown))
                    Camera.FocalDistance /= 1.1f;
                if (kstate.IsKeyDown(Key.Home))
                    Camera.EyeSeparation += .005f;
                if (kstate.IsKeyDown(Key.End))
                    Camera.EyeSeparation -= .005f;
            }

            // CameraStereoMode

            Camera.RotateRight(δx * .2f * MouseSensitivityFactor);
            Camera.RotateUp(δy * .2f * MouseSensitivityFactor);

            _mousex = mstate.X;
            _mousey = mstate.Y;
            _mousescroll = mstate.WheelPrecise;

            //System.Windows.Forms.Cursor.Position = new Point(X + (Width / 2), Y + (Height / 2));
        }

        public void Invoke(Action a)
        {
            lock (_queue)
                if (a != null)
                    _queue.Enqueue(a);
        }

        public static void ShowHelp() => MessageBox.Show(@"
---------------- KEYBOARD SHORTCUTS ----------------
[ESC] Pause
[SHIFT][ESC] Exit
[H] Show this help window
[F] Take screenshot
[SHIFT][F] Take HDR screenshot

[W] Move forwards
[A] Move left
[D] Move right
[S] Move backwards
[SPACE] Move up
[CTRL] Move down
[C] Fast movement
[SHIFT] Slow movement
[Q] Zoom out
[E] Zoom in

[4] Toggle Stereoscopic display
[6] Toggle HUD
[P.Up] Increase focal distance (Stereoscopic only)
[P.Down] Decrease focal distance (Stereoscopic only)
[Home] Increase eye separation (Stereoscopic only)
[End] Derease eye separation (Stereoscopic only)

[X] Cycle visual effects
".Trim());
    }
}

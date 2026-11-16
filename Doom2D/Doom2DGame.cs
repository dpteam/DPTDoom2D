using System;
using Doom2D.Engine;
using Doom2D.Game;
using DGame = Doom2D.Game.Game;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Doom2D
{
    /// <summary>
    /// The FNA host: window, fixed 20 Hz tick (DELAY 50), input, and one
    /// framebuffer blit per frame. All gameplay lives in Game/Game.cs.
    /// </summary>
    public sealed class Doom2DGame : Microsoft.Xna.Framework.Game
    {
        readonly GraphicsDeviceManager graphics;
        SpriteBatch spriteBatch;
        Screen screen;

        public Doom2DGame()
        {
            graphics = new GraphicsDeviceManager(this);

            graphics.PreferredBackBufferWidth = Vga.SCRW * Program.Scale;
            graphics.PreferredBackBufferHeight = Vga.SCRH * Program.Scale;
            graphics.PreferMultiSampling = false;
            graphics.SynchronizeWithVerticalRetrace = false;
            graphics.ApplyChanges();

            IsFixedTimeStep = true;
            TargetElapsedTime = TimeSpan.FromMilliseconds(Glob.Delay);

            Window.Title = "Doom 2D";
            Window.AllowUserResizing = false;
        }

        protected override void Initialize()
        {
            Vga.Init(Vga.SCRW, Vga.SCRH);

            Wad.Startup();
            Wad.AddWad(System.IO.Path.Combine(Program.DataDirectory, "doom2d.wad"));
            for (int i = 0; i < Config.ExtraWads.Count; ++i)
                if (System.IO.File.Exists(Config.ExtraWads[i])) Wad.AddWad(Config.ExtraWads[i]);
            Wad.InitWads();
            Palette.Load();
            Palette.SetGamma(Config.Gamma);

            // config.c targets: warp selects the level, mon enables monsters,
            // fullscr starts windowed or fullscreen.
            DGame.Warp = Config.Warp;
            Monsters.NoMon = Config.NoMonsters;
            Program.Fullscreen = Config.Fullscreen;

            base.Initialize();
        }

        protected override void LoadContent()
        {
            spriteBatch = new SpriteBatch(GraphicsDevice);
            screen = new Screen(GraphicsDevice, Vga.SCRW, Vga.SCRH);

            DGame.Init();
            World.LoadLightningSounds();
            Sound.Init();
            Music.Init();

            Program.ToggleFullscreenRequested += ApplyFullscreen;
            Program.ScreenshotRequested += SaveScreenshot;

            base.LoadContent();
        }

        void ApplyFullscreen()
        {
            graphics.IsFullScreen = Program.Fullscreen;
            graphics.ApplyChanges();
        }

        void SaveScreenshot()
        {
            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Screenshots");
            System.IO.Directory.CreateDirectory(dir);
            int n = 1;
            string path;
            do { path = System.IO.Path.Combine(dir, string.Format("shot{0:0000}.bmp", n++)); }
            while (System.IO.File.Exists(path));

            using (System.IO.FileStream fs = System.IO.File.Create(path))
                screen.SaveBmp(fs);
        }

        protected override void Update(GameTime gameTime)
        {
            int last = Kbd.LastKey;
            Kbd.Update();

            if (Kbd.LastKey != last && Kbd.LastKey != 0) Menu.KeyProc(Kbd.LastKey);

            DGame.Act();
            Music.Update();

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);

            DGame.Draw();

            screen.Upload();
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque,
                SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            screen.Draw(spriteBatch, new Rectangle(0, 0,
                GraphicsDevice.PresentationParameters.BackBufferWidth,
                GraphicsDevice.PresentationParameters.BackBufferHeight));
            spriteBatch.End();

            base.Draw(gameTime);
        }
    }
}

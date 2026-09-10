using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace StoreChase
{
    public class Game1 : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch = null!;
        private SpriteFont? _font;

        private GridMap _map = null!;
        private Player _player = null!;
        private Critter _critter = null!;
        private Texture2D _pixel = null!;

        private bool _won = false;
        private float _elapsedTime = 0f;

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
        }

        protected override void Initialize()
        {
            _map = new GridMap(cellSize: 32);
            _graphics.PreferredBackBufferWidth = _map.Width * _map.CellSize;
            _graphics.PreferredBackBufferHeight = _map.Height * _map.CellSize + 40; // extra for UI bar
            _graphics.ApplyChanges();

            _player = new Player(new GridPos(1, 1), _map.CellSize);
            _critter = new Critter(new GridPos(_map.Width - 2, _map.Height - 2), _map.CellSize);

            base.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);

            _pixel = new Texture2D(GraphicsDevice, 1, 1);
            _pixel.SetData(new[] { Color.White });

            // Optional: load a font if you add one via the Content Pipeline (see README).
            // _font = Content.Load<SpriteFont>("DefaultFont");
        }

        protected override void Update(GameTime gameTime)
        {
            if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
                Keyboard.GetState().IsKeyDown(Keys.Escape))
                Exit();

            if (!_won)
            {
                _elapsedTime += (float)gameTime.ElapsedGameTime.TotalSeconds;
                _player.Update(gameTime, _map, _map.CellSize);
                _critter.Update(gameTime, _map, _map.CellSize, _player.Cell);

                if (_critter.Caught) _won = true;
            }
            else
            {
                if (Keyboard.GetState().IsKeyDown(Keys.R)) Restart();
            }

            base.Update(gameTime);
        }

        private void Restart()
        {
            _player = new Player(new GridPos(1, 1), _map.CellSize);
            _critter = new Critter(new GridPos(_map.Width - 2, _map.Height - 2), _map.CellSize);
            _elapsedTime = 0f;
            _won = false;
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(new Color(30, 30, 35));

            _spriteBatch.Begin();

            DrawMap();
            _critter.Draw(_spriteBatch, _pixel, _map.CellSize);
            _player.Draw(_spriteBatch, _pixel, _map.CellSize);
            DrawUiBar();

            _spriteBatch.End();

            base.Draw(gameTime);
        }

        private void DrawMap()
        {
            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    if (_map[x, y] == 1)
                    {
                        var rect = new Rectangle(x * _map.CellSize, y * _map.CellSize, _map.CellSize, _map.CellSize);
                        _spriteBatch.Draw(_pixel, rect, new Color(90, 60, 40)); // "shelf" color
                    }
                }
            }
        }

        private void DrawUiBar()
        {
            int barY = _map.Height * _map.CellSize;
            int barHeight = 40;
            var barRect = new Rectangle(0, barY, _map.Width * _map.CellSize, barHeight);
            _spriteBatch.Draw(_pixel, barRect, new Color(15, 15, 18));

            // No font loaded yet, so the "UI" is color blocks — see README for
            // how to add a SpriteFont and draw real text (timer, win message).
            var indicatorColor = _won ? Color.LimeGreen : Color.Gray;
            _spriteBatch.Draw(_pixel, new Rectangle(10, barY + 10, 20, 20), indicatorColor);

            if (_font != null)
            {
                string status = _won ? $"Caught it! Time: {_elapsedTime:F1}s  (R to restart)"
                                      : $"Time: {_elapsedTime:F1}s";
                _spriteBatch.DrawString(_font, status, new Vector2(40, barY + 10), Color.White);
            }
        }
    }
}

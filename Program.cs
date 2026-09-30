// Entry point: create the game and hand control to MonoGame's main loop.
// All the interesting wiring (content, systems, the first round) happens in
// Game1.Initialize / Game1.LoadContent / Game1.StartRound.
using var game = new StoreChase.Game1();
game.Run();

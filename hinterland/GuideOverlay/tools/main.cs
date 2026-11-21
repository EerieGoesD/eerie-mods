// Hinterland Optimizer overlay - loader.
// The game's main.cs already tries to load tools/main.cs, so no game file is changed.
// Delete the tools folder to remove the mod.

// write console.log in the game folder (flushed after every line), so script errors can be checked
setLogMode(1);

exec("./optimizer.cs");

package OptimizerPackage
{
   function onStart()
   {
      Parent::onStart();
      schedule(3000, 0, OptInit);
   }
};
activatePackage(OptimizerPackage);

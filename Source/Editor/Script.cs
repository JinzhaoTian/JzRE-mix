// JzRE.Script — managed base class for user scripts.
// Mirrors FlaxEngine's Script.cs: provides virtual lifecycle methods
// that are called by the native ScriptingEngine each frame.
//
// Usage:
//   public class MyScript : JzRE.Script
//   {
//       public override void OnUpdate(float deltaTime)
//       {
//           // Called every frame
//       }
//   }

namespace JzRE;

public partial class Script : JzObject
{
    public bool Enabled { get; set; } = true;
}

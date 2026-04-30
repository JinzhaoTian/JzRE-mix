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

public partial class Script : Object
{
    public bool Enabled { get; set; } = true;

    /// <summary>Called when the script becomes active in the scene.</summary>
    public virtual void OnEnable() { }

    /// <summary>Called when the script becomes inactive or is removed.</summary>
    public virtual void OnDisable() { }

    /// <summary>Called every frame. deltaTime is seconds since last frame.</summary>
    public virtual void OnUpdate(float deltaTime) { }

    /// <summary>Called just before the script and its native peer are destroyed.</summary>
    public virtual void OnDestroy() { }
}

using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.Utilities;

namespace Brickcraft.Tests
{
    /// <summary>The classic input's keys as Input System paths (KeyPaths), what GameInput reads.</summary>
    public class KeyPathsTests
    {
        [Test]
        public void EveryDefaultKeyHasAPathAndComesBack() {
            foreach (GameInput.Binding binding in GameInput.Bindings) {
                foreach (KeyCode key in new[] { binding.defaultKey, binding.defaultAltKey }) {
                    if (key == KeyCode.None) {
                        continue;
                    }
                    string path = KeyPaths.PathOf(key);
                    Assert.IsNotNull(path, binding.button + "'s " + key);
                    Assert.AreEqual(key, KeyPaths.KeyOf(path), path);
                }
            }
        }

        [Test]
        public void EveryPathIsAControlOfItsDevice() {
            for (KeyCode key = KeyCode.None; key <= KeyCode.Mouse6; key++) {
                string path = KeyPaths.PathOf(key);
                if (path == null) {
                    continue;
                }
                // "<Keyboard>/q": the layout and its control
                string layoutName = path.Substring(1, path.IndexOf('>') - 1);
                string control = path.Substring(path.IndexOf('/') + 1);
                InputControlLayout layout = InputSystem.LoadLayout(layoutName);
                Assert.IsNotNull(layout, path);
                Assert.IsTrue(layout.FindControl(new InternedString(control)).HasValue, path + " isn't a control of " + layoutName);
            }
        }
    }
}

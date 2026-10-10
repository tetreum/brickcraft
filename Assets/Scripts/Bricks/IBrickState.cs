namespace Brickcraft.Bricks
{
    /// <summary>
    /// A component of a brick model that shows the brick's state (Brick.state), like a door being open.
    /// Every one in a brick's object hears about it when the brick appears and whenever it changes.
    /// </summary>
    public interface IBrickState
    {
        /// <param name="animate">False when the brick just appeared: it shows it right away.</param>
        void ShowState(int state, bool animate);
    }
}

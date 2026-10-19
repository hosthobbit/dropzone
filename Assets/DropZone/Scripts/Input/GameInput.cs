using UnityEngine;
using UnityEngine.InputSystem;

namespace DropZone
{
    /// <summary>
    /// Board controls for keyboard, mouse, touch and gamepad, built in code with the Input System.
    /// Menus use the EventSystem's own UI actions; these are only read during play.
    /// </summary>
    public class GameInput
    {
        public readonly InputActionAsset Asset;
        public readonly InputActionMap Board;
        public readonly InputAction Move, Confirm, Cancel, Pause, Bomb, Swap, Point, Press;

        public GameInput()
        {
            Asset = ScriptableObject.CreateInstance<InputActionAsset>();
            Board = Asset.AddActionMap("Board");

            Move = Board.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow").With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Move.AddCompositeBinding("2DVector").With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s").With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddBinding("<Gamepad>/dpad");
            Move.AddBinding("<Gamepad>/leftStick");

            Confirm = Board.AddAction("Confirm", InputActionType.Button);
            Confirm.AddBinding("<Keyboard>/enter"); Confirm.AddBinding("<Keyboard>/numpadEnter"); Confirm.AddBinding("<Keyboard>/space");
            Confirm.AddBinding("<Gamepad>/buttonSouth");

            Cancel = Board.AddAction("Cancel", InputActionType.Button);
            Cancel.AddBinding("<Keyboard>/backspace"); Cancel.AddBinding("<Gamepad>/buttonEast"); Cancel.AddBinding("<Mouse>/rightButton");

            Pause = Board.AddAction("Pause", InputActionType.Button);
            Pause.AddBinding("<Keyboard>/escape"); Pause.AddBinding("<Keyboard>/p"); Pause.AddBinding("<Gamepad>/start");

            Bomb = Board.AddAction("Bomb", InputActionType.Button);
            Bomb.AddBinding("<Keyboard>/b"); Bomb.AddBinding("<Keyboard>/1"); Bomb.AddBinding("<Gamepad>/buttonWest");

            Swap = Board.AddAction("Swap", InputActionType.Button);
            Swap.AddBinding("<Keyboard>/x"); Swap.AddBinding("<Keyboard>/2"); Swap.AddBinding("<Gamepad>/buttonNorth");

            Point = Board.AddAction("Point", InputActionType.PassThrough, "<Pointer>/position");
            Press = Board.AddAction("Press", InputActionType.Button, "<Pointer>/press");

            Asset.Enable();
        }

        // repeat for held directions (keys, d-pad, stick)
        Vector2Int held;
        float nextRepeat;

        /// <summary>A step left/right/up/down this frame, with key-repeat while held.</summary>
        public Vector2Int MoveStep(float time)
        {
            var v = Move.ReadValue<Vector2>();
            var dir = new Vector2Int(Mathf.Abs(v.x) > 0.5f ? (int)Mathf.Sign(v.x) : 0, Mathf.Abs(v.y) > 0.5f ? (int)Mathf.Sign(v.y) : 0);
            if (dir == Vector2Int.zero) { held = dir; return dir; }
            if (dir != held) { held = dir; nextRepeat = time + 0.35f; return dir; }
            if (time >= nextRepeat) { nextRepeat = time + 0.11f; return dir; }
            return Vector2Int.zero;
        }

        /// <summary>Was the last pointer press a touch (so taps preview first, a second tap drops)?</summary>
        public bool PointerIsTouch => Press.activeControl != null && Press.activeControl.device is Touchscreen;
    }
}

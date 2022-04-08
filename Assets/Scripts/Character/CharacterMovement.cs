using BrickHill;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using Utils;

public class CharacterMovement : MonoBehaviour
{
    public CharacterMain characterMain;
    public CharacterSounds characterSounds;

    public float PlayerSpeed = 16.0f;
    public float Gravity;
    public float JumpPower;

    public bool isMoving;

    public Transform Cam;
    public CharacterCamera characterCamera;

    private CharacterController cc;
    private float horizontal;
    private float vertical;
    private float ySpeed;
    private bool jumpButton;

    private bool inAir;
    private float peakHeight; // used for fall height detection

    private float lastPacketTime = 0;
    private Vector3 lastSentPosition;

    private Ray cursorRay;
    private RaycastHit cursorHit;
    private Map.Brick clickableBrick;

    private void Start() {
        InputHelper.controls.Player.Jump.started += ctx => jumpButton = true;
        InputHelper.controls.Player.Jump.canceled += ctx => jumpButton = false;

        InputHelper.controls.Player.Click.performed += ctx => TryClick();

        cc = GetComponent<CharacterController>();
        lastSentPosition = transform.position;
    }

    private void Update() {
        if (Input.anyKeyDown && InputHelper.controls.asset.enabled) {
            SendInput(false);
        }

        float _speed = (40f * Time.deltaTime) * (PlayerSpeed / 15f);
        float _jump = JumpPower * 8;

        // Movement
        Vector3 CameraForward = Cam.forward;
        CameraForward.y = 0;
        CameraForward.Normalize();
        Vector3 CameraRight = Cam.right;
        CameraRight.y = 0;
        CameraRight.Normalize();

        Vector2 input = InputHelper.controls.Player.Move.ReadValue<Vector2>();

        Vector3 movement = CameraForward * input.y + CameraRight * input.x;
        movement.Normalize(); // dont do if using controller

        isMoving = movement != Vector3.zero;
        characterMain.characterAnimator.SetBool("Is Running", isMoving);

        bool isRotating = false;

        if (isMoving && !characterCamera.firstPerson) { // rotate towards movement direction while in third person
            transform.rotation = Helper.Damp(transform.rotation, Quaternion.LookRotation(movement), 0.0025f, Time.deltaTime); // face movement direction
        } else if (characterCamera.firstPerson) { // rotate towards camera direction while in first person
            float angle = Quaternion.Angle( Quaternion.Euler(0, Cam.eulerAngles.y, 0), Quaternion.Euler(0, transform.eulerAngles.y, 0) );
            isRotating = angle > 0.001f;

            transform.eulerAngles = new Vector3(0, Cam.eulerAngles.y, 0);
        }
        
        if (cc.isGrounded) {
            ySpeed = -1f;
            if (jumpButton) {
                ySpeed = _jump;
                characterMain.characterAnimator.SetTrigger("Jump");
                characterSounds.PlaySound("jump", 1.0f, transform.position);
            }
        }

        inAir = !cc.isGrounded;

        // player speed
        movement *= _speed;

        ySpeed -= Gravity * Time.deltaTime; // gravity

        movement.y = ySpeed;
        movement.y *= Time.deltaTime;
        
        // move
        cc.Move(movement);
        characterMain.UpdateFigure();

        characterMain.characterAnimator.SetBool("Is Grounded", cc.isGrounded);

        characterSounds.walking = (isMoving && cc.isGrounded); // only play footsteps when moving on the ground

        // calculate peak height while in air
        if (!cc.isGrounded && transform.position.y > peakHeight) peakHeight = transform.position.y;

        if ((isMoving || isRotating) || !cc.isGrounded) {
            // send movement packet after player has moved or rotated
            if (Time.time - lastPacketTime > 0.04f) { // send 25 packets/s
                characterMain.SendMovementPacket();
                lastPacketTime = Time.time;
                lastSentPosition = transform.position.Round(100);
            }
        }
        
        if (cc.isGrounded && inAir) {
            // this should only run as soon as we hit the ground
            characterMain.SendMovementPacket();
            lastPacketTime = Time.time;
            lastSentPosition = transform.position.Round(100);
            inAir = false;

            // only play fall sound if player has fallen 10 or more studs
            float fallHeight = peakHeight - transform.position.y;
            if (fallHeight >= 5)
                characterSounds.PlaySound("fall", Mathf.Clamp(fallHeight / 20f, 0f, 1f), transform.position);
            peakHeight = 0;
        }

        // Change cursor
        cursorRay = characterMain.main.map.MainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
        clickableBrick = null;
        int cursorIndex = 0;
        if (Physics.Raycast(cursorRay, out cursorHit, 2000)) {
            if (cursorHit.collider.CompareTag("Clickable")) {
                Map.Brick b = characterMain.main.map.LoadedMap.GetBrick(int.Parse(cursorHit.collider.gameObject.name, CultureInfo.InvariantCulture)); // what the heck
                if (b != null) {
                    Vector3 brickPos = b.Position.SwapYZ();
                    brickPos.x *= -1;
                    float dist = Mathf.Pow(brickPos.x - lastSentPosition.x, 2) + Mathf.Pow(brickPos.y - lastSentPosition.y, 2) + Mathf.Pow(brickPos.z - lastSentPosition.z, 2); // ok
                    if (b.Clickable && dist <= b.ClickDistance) {
                        // yes we can click the brick
                        clickableBrick = b;
                        cursorIndex = 1;
                    }
                }
            }
        }
        characterMain.main.ui.SetCursor(cursorIndex);
    }

    public void TryClick () {
        SendInput(true);

        if (clickableBrick != null) {
            characterMain.main.SendClickPacket(clickableBrick);
            clickableBrick = null;
        }
    }

    public void SendInput (bool mouse) {
        string key = GetInput();
        if (mouse || key != "none") characterMain.SendInputPacket(mouse, key);
    }

    public void SetPosition (Vector3 position) {
        transform.position = position;
        Physics.SyncTransforms();
    }

    private string GetInput () {
        if (Input.GetKeyDown(KeyCode.Space)) return "space"; // space
        if (Input.GetKeyDown(KeyCode.Return)) return "enter"; // enter
        if (Input.GetKeyDown(KeyCode.LeftShift)||Input.GetKeyDown(KeyCode.RightShift)) return "shift"; // shift
        if (Input.GetKeyDown(KeyCode.LeftControl)||Input.GetKeyDown(KeyCode.RightControl)) return "control"; // ctrl
        if (Input.GetKeyDown(KeyCode.Backspace)) return "backspace"; // backspace

        string kp = Input.inputString;
        if (kp.Length > 0 && char.IsLetterOrDigit(kp, 0)) return kp.Substring(0,1).ToLower(); // alphanumeric keys
        return "none";
    }
}

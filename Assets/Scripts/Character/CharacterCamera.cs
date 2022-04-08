using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Utils;

public class CharacterCamera : MonoBehaviour
{
    public CharacterMain main;

    public float CameraDistance;
    public float MaxCameraDistance = 10f;
    public float MinCameraDistance = 1f;
    public float CameraSensitivity;
    public Vector3 CameraTargetPosition;
    public float ZoomSensitivity;
    public bool InvertZoom;
    public Transform CameraTarget;
    public Transform Player;

    public LayerMask CameraRaycastMask;
    public LayerMask DefaultViewMask;
    public LayerMask FirstPersonViewMask;

    private bool mouseLookButton;
    public bool firstPerson;

    private Camera cam;
    private float camDist;
    private Vector3 camRot;

    private bool inEditor = false;

    private void Awake () {
        cam = GetComponent<Camera>();
        inEditor = Application.platform == RuntimePlatform.LinuxEditor; // linux editor cursor locking is stupid so i disable cursor locking for ez testing
    }

    private void Start () {
        InputHelper.controls.Player.EnableMouse.started += ctx => mouseLookButton = true;
        InputHelper.controls.Player.EnableMouse.canceled += ctx => mouseLookButton = false;

        CameraSensitivity = SettingsManager.PlayerSettings.MouseSensitivity;
        ZoomSensitivity = SettingsManager.PlayerSettings.ZoomSensitivity;
    }

    private void Update () {
        CameraTarget.position = Player.position + CameraTargetPosition; // get camera into position

        float zoom = InputHelper.controls.Player.Zoom.ReadValue<float>();
        if (zoom != 0) {
            if (InvertZoom) zoom *= -1;
            CameraDistance = Mathf.Clamp(CameraDistance + zoom * ZoomSensitivity, MinCameraDistance, MaxCameraDistance);
        }
        

        if (CameraDistance == 0) {
            mouseLookButton = true;
            if (!firstPerson) {
                //cam.cullingMask = FirstPersonViewMask;
                main.SetVisibility(false);
                firstPerson = true;
                PlayerMain.instance.ui.CrosshairVisible = true;
            }
        } else if (firstPerson) {
            // exit first person
            //cam.cullingMask = DefaultViewMask;
            main.SetVisibility(true);
            mouseLookButton = false;
            PlayerMain.instance.ui.CrosshairVisible = false;
            firstPerson = false;
        }

        if (mouseLookButton) {
            if (!inEditor) Cursor.lockState = CursorLockMode.Locked;
            PlayerMain.instance.ui.CrosshairVisible = true;
            Vector3 rot = Helper.SwapXY(InputHelper.controls.Player.Look.ReadValue<Vector2>());
            //Vector3 rot = new Vector3(-Input.GetAxis("Mouse Y"), Input.GetAxis("Mouse X"), 0f);
            camRot += rot * CameraSensitivity;
            camRot.x = Mathf.Clamp(camRot.x, -89, 89);
            CameraTarget.eulerAngles = camRot;
        } else {
            if (!inEditor) Cursor.lockState = CursorLockMode.None;
            if (!firstPerson) PlayerMain.instance.ui.CrosshairVisible = false;
        }

        Vector3 desiredCamPos = CameraTarget.TransformPoint(0,0,-(CameraDistance+1));

        if (Physics.Linecast(CameraTarget.position, desiredCamPos, out RaycastHit hit, CameraRaycastMask)) {
            camDist = Mathf.Clamp((hit.distance * 0.87f), MinCameraDistance, MaxCameraDistance);
        } else {
            camDist = CameraDistance;
        }

        transform.rotation = CameraTarget.rotation;
        transform.position = CameraTarget.TransformPoint(0,0,-camDist);
    }
}

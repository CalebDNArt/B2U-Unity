using B2U;
using System.Collections;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public static bool playerControl = true;
    public bool localControl = true;
    public bool interactingWithStation = false;

    public static Vector3 playerPosition;

    public Camera playerCam;
    public CameraController camController;

    private Vector3 defaultCamPos = new Vector3(0f, 0.5f, 0f);
    private Quaternion defaultCamRot = Quaternion.identity;

    private Vector3 lastCamPos;
    private Quaternion lastCamRot;

    public CharacterController characterController;
    public Transform mainCam;

    public B2URSUVMono testLiquidRSUV = null;

    public float moveSpeed = 10f;

    public float lookSpeed = 0.12f;
    public float jumpHeight = 1f;
    public float gravity = -9.81f;

    private float yVelocity = 0f;
    private float camPitch = 0f;
    private float camYaw = 0f;

    private RaycastHit interactionHit;
    public LayerMask interactionLayer;

    private SampleSceneInputActions actions => SceneController.actions;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        camYaw = transform.eulerAngles.y;
    }

    void Update()
    {
        if (actions == null) return;

        playerPosition = transform.position;

        if (playerControl && localControl)
        {
            if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;

            Vector2 moveInput = actions.Player.Move.ReadValue<Vector2>();
            Vector2 lookInput = actions.Player.Look.ReadValue<Vector2>();

            camYaw += lookInput.x * lookSpeed;
            transform.rotation = Quaternion.Euler(0f, camYaw, 0f);

            Vector3 moveVector = ((transform.forward * moveInput.y) + (transform.right * moveInput.x)).normalized * moveSpeed;
            Vector3 flow = Vector3.zero;

            if (testLiquidRSUV != null)
            {
                flow = testLiquidRSUV.GetLiquidPush(playerPosition - new Vector3(0f, 1f, 0f), 2.5f, true);
                moveVector += flow;
            }

            if (characterController.isGrounded)
            {
                if (actions.Player.Jump.triggered)
                {
                    yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                }
                else
                {
                    yVelocity = -2f;
                }
            }
            else
            {
                if (yVelocity > -55f)
                {
                    yVelocity += gravity * Time.deltaTime;
                }
            }

            moveVector.y = yVelocity;
            characterController.Move(moveVector * Time.deltaTime);

            camPitch = Mathf.Clamp(camPitch - (lookInput.y * lookSpeed), -85f, 85f);
            mainCam.localRotation = Quaternion.Euler(camPitch, 0f, 0f);
        }
    }

    public void SnapCamera(Vector3 targetPos, Quaternion targetRot)
    {
        playerCam.transform.parent = null;
        lastCamPos = playerCam.transform.position;
        lastCamRot = playerCam.transform.rotation;
        playerControl = false;
        StartCoroutine(SnapCameraRoutine(targetPos, targetRot));
    }

    public void ReturnCamera()
    {
        StartCoroutine(ReturnCamRoutine());
    }

    public IEnumerator SnapCameraRoutine(Vector3 targetPos, Quaternion targetRot)
    {
        float time = 0f;
        Vector3 startPos = playerCam.transform.position;
        Quaternion startRot = playerCam.transform.rotation;
        while (time < 0.5f)
        {
            float t = time / 0.5f;

            playerCam.transform.position = Vector3.Lerp(startPos, targetPos, t);
            playerCam.transform.rotation = Quaternion.Slerp(startRot, targetRot, t);

            time += Time.deltaTime;
            yield return null;
        }
    }

    public IEnumerator ReturnCamRoutine()
    {
        playerCam.transform.parent = this.gameObject.transform;
        float time = 0f;
        Vector3 startPos = playerCam.transform.localPosition;
        Quaternion startRot = playerCam.transform.rotation;
        while (time < 0.5f)
        {
            float t = time / 0.5f;

            playerCam.transform.localPosition = Vector3.Lerp(startPos, defaultCamPos, t);
            playerCam.transform.rotation = Quaternion.Slerp(startRot, lastCamRot, t);

            time += Time.deltaTime;
            yield return null;
        }
        playerCam.transform.localPosition = defaultCamPos;
        playerCam.transform.rotation = lastCamRot;
        playerControl = true;
    }
}
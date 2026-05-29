using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IcoSphere {
    public class RvtTest : MonoBehaviour {
        [SerializeField] private Camera cam;
        [SerializeField] private GameObject terrainBase;
        [SerializeField] private GameObject terrainRvt;
        [SerializeField] private float mouseSensitivity = 2.0f;
        [SerializeField] private float verticalLimit = 80.0f;
        [SerializeField] private float walkSpeed = 10.0f;
        [SerializeField] private float liftSpeed = 10.0f;

        private float xRotation = 0.0f;
        private float yRotation = 0.0f;
        private bool baseMode;
        private float angle;

        private void Awake() {
            baseMode = false;
            terrainBase.SetActive(baseMode);
            terrainRvt.SetActive(!baseMode);

            angle = 0.0f;
            cam.transform.rotation = Quaternion.Euler(0.0f, angle, 0.0f);
        }

        private void Update() {
            float t = Time.deltaTime;
            if (Input.GetKeyDown(KeyCode.Space)) {
                baseMode = !baseMode;
                terrainBase.SetActive(baseMode);
                terrainRvt.SetActive(!baseMode);
            }
            CamCtrlUpdate();
        }

        private void CamCtrlUpdate() {
            Transform tf = cam.transform;
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
            tf.Rotate(Vector3.up * mouseX);

            yRotation += mouseX;
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -verticalLimit, verticalLimit);
            tf.rotation = Quaternion.Euler(xRotation, yRotation, 0.0f);

            float horizontal = Input.GetAxis("Horizontal");
            float vertical = Input.GetAxis("Vertical");
            Vector3 forward = tf.forward;
            Vector3 right = tf.right;
            forward.y = 0.0f;
            right.y = 0.0f;
            forward.Normalize();
            right.Normalize();

            Vector3 move = Time.deltaTime * walkSpeed * (forward * vertical + right * horizontal);

            float lift = 0.0f;
            if (Input.GetKey(KeyCode.Q)) {
                lift = -liftSpeed;
            }
            if (Input.GetKey(KeyCode.E)) {
                lift = liftSpeed;
            }
            move.y = lift * Time.deltaTime;
            tf.Translate(move, Space.World);
        }
    }
}

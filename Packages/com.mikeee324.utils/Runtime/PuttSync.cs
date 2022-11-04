
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace mikeee324.Utils
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class PuttSync : UdonSharpBehaviour
    {
        #region Public Settings
        [Header("Sync Settings")]
        [Range(0, 1f), Tooltip("How long the object should keep syncing fast for after requesting a fast sync")]
        public float fastSyncTimeout = 0.5f;
        [Tooltip("This lets you define a curve to scale back the speed of fast updates based on the number on players in the instance. You can leave this empty and a default curve will be applied when the game loads")]
        public AnimationCurve fastSyncIntervalCurve;
        [Tooltip("Will periodically try to send a sync in the background every 3-10 seconds. Off by default as build 1258 should solve later joiner issues (Will only actually sync if this.transform has been touched)")]
        public bool enableSlowSync = false;

        [Header("Pickup Settings")]
        [Tooltip("If enabled PuttSync will operate similar to VRC Object Sync")]
        public bool syncPositionAndRot = true;
        [Tooltip("Monitors a VRCPickup on the same GameObject. When it is picked up by a player fast syncs will be enabled automatically.")]
        public bool monitorPickupEvents = true;
        [Tooltip("Should this object be returned to its spawn position after players let go of it")]
        public bool returnAfterDrop = false;
        [Range(2f, 30f), Tooltip("If ReturnAfterDrop is enabled this object will be put back into its original position after this many seconds of not being held")]
        public float returnAfterDropTime = 10f;
        [Tooltip("Should the object be respawned if it goes below the height specified below?")]
        public bool autoRespawn = true;
        [Tooltip("The minimum height that this object can go to before being respawned (if enabled)")]
        public float autoRespawnHeight = -100f;
        public bool canManageRigidbodyState = true;
        #endregion

        #region Private References
        private Rigidbody objectRB;
        private VRCPickup pickup;
        #endregion

        #region Synced Vars
        /// <summary>
        /// The last known position of this object from the network (synced between players)
        /// </summary>
        [UdonSynced]
        private Vector3 syncPosition;
        /// <summary>
        /// The last known rotation of this object from the network (synced between players)
        /// </summary>
        [UdonSynced]
        private Quaternion syncRotation;
        /// <summary>
        /// The respawn position for this object
        /// </summary>
        private Vector3 originalPosition;
        /// <summary>
        /// The respawn rotation for this object
        /// </summary>
        private Quaternion originalRotation;
        /// <summary>
        /// Is the owner holding this object?
        /// </summary>
        [UdonSynced]
        private bool playerIsHoldingObject = false;
        #endregion

        #region Internal Working Vars
        /// <summary>
        /// A local timer that stores how much time has passed since the owner last tried to sync this object
        /// </summary>
        private float syncTimer = 0f;
        /// <summary>
        /// A local timer that tracks how much longer this object can send fast updates for
        /// </summary>
        private float currentFastSyncTimeout = 0f;
        /// <summary>
        /// Amount of time (in seconds) between fast object syncs for this object (Scaled based on player count using fastSyncIntervalCurve)
        /// </summary>
        private float fastSyncInterval = 0.05f;
        /// <summary>
        /// Amount of time (in seconds) between slow object syncs for this object (Is set to a random value between 3-10 each time)
        /// </summary>
        private float slowSyncInterval = 1f;
        /// <summary>
        /// A local timer that tracks how long since the owner of this object dropped it
        /// </summary>
        private float timeSincePlayerDroppedObject = -1f;
        private bool rigidBodyUseGravity = false;
        private bool rigidBodyisKinematic = false;
        #endregion

        void Start()
        {
            pickup = GetComponent<VRCPickup>();
            objectRB = GetComponent<Rigidbody>();

            originalPosition = this.transform.position;
            originalRotation = this.transform.rotation;

            if (fastSyncIntervalCurve == null || fastSyncIntervalCurve.length == 0)
            {
                fastSyncIntervalCurve = new AnimationCurve();
                fastSyncIntervalCurve.AddKey(0f, 0.05f);
                fastSyncIntervalCurve.AddKey(20f, 0.05f);
                fastSyncIntervalCurve.AddKey(40f, 0.1f);
                fastSyncIntervalCurve.AddKey(82f, 0.15f);
            }

            fastSyncInterval = fastSyncIntervalCurve.Evaluate(VRCPlayerApi.GetPlayerCount());
        }

        void Update()
        {
            if (Networking.LocalPlayer == null) return;

            if (!Networking.LocalPlayer.IsOwner(gameObject))
            {
                // Disable pickup for other players if theft is disabled
                if (pickup != null)
                {
                    bool newPickupState = true;
                    if (pickup.DisallowTheft && playerIsHoldingObject)
                        newPickupState = false;

                    if (newPickupState != pickup.pickupable)
                        pickup.pickupable = newPickupState;
                }

                if (canManageRigidbodyState && objectRB != null && !objectRB.isKinematic)
                {
                    // If we are being moved by something else make the rigidbody kinematic
                    objectRB.useGravity = false;
                    objectRB.isKinematic = true;
                }

                if (syncPositionAndRot)
                {
                    if ((transform.position - syncPosition).magnitude > 0.0001f || Quaternion.Angle(transform.rotation, syncRotation) > 0.001f)
                    {
                        // Try to smooth out the lerps
                        float lerpProgress = 1.0f - Mathf.Pow(0.001f, Time.deltaTime);
                        //float lerpProgress = Time.deltaTime / fastSyncInterval;

                        // Move this object to where we are told
                        transform.position = Vector3.Lerp(transform.position, syncPosition, lerpProgress);
                        transform.rotation = Quaternion.Slerp(transform.rotation, syncRotation, lerpProgress);
                    }
                }
                return;
            }

            // Enable pickup for the owner
            if (pickup != null && !pickup.pickupable)
                pickup.pickupable = true;

            if (autoRespawn && this.transform.position.y < autoRespawnHeight)
            {
                Utils.Log(this, "Object dropped below respawn height");
                Respawn();
            }

            if (returnAfterDrop)
            {
                if (playerIsHoldingObject)
                    timeSincePlayerDroppedObject = 0f;
                else if (timeSincePlayerDroppedObject < returnAfterDropTime)
                    timeSincePlayerDroppedObject += Time.deltaTime;
                else if (timeSincePlayerDroppedObject >= returnAfterDropTime)
                    Respawn();
            }

            if (monitorPickupEvents && playerIsHoldingObject)
            {
                // If player is holding item then enable fast sync until they let go
                currentFastSyncTimeout = fastSyncTimeout;
            }

            // Always store latest position data
            syncPosition = this.transform.position;
            syncRotation = this.transform.rotation;

            // Work out which timer we are currently using (Fast/Slow/None)
            float currentSyncInterval = -1;
            if (currentFastSyncTimeout > 0f)
                currentSyncInterval = fastSyncInterval;
            else if (enableSlowSync)
                currentSyncInterval = slowSyncInterval;

            if (currentSyncInterval >= 0f)
            {
                if (syncTimer > currentSyncInterval)
                {
                    // Send a network sync if enough time has passed and the object has moved/rotated (seems to work fine if the parent of this object moves also)
                    if (this.transform.hasChanged || currentFastSyncTimeout > 0f)
                    {
                        RequestSerialization();
                        this.transform.hasChanged = false;
                    }

                    // Reset the timer
                    syncTimer = 0f;

                    // Randomize the slow sync interval each time we do one so they don't all happen at the same time
                    if (currentFastSyncTimeout <= 0f)
                        slowSyncInterval = Random.Range(3f, 10f);
                }

                syncTimer += Time.deltaTime;

                if (currentFastSyncTimeout > 0f)
                    currentFastSyncTimeout -= Time.deltaTime;
            }
        }

        public override void OnPickup()
        {
            playerIsHoldingObject = true;
            if (monitorPickupEvents)
            {
                Utils.SetOwner(Networking.LocalPlayer, gameObject);
                RequestFastSync();
            }
        }

        public override void OnDrop()
        {
            playerIsHoldingObject = false;

            // If we can manage the rigidbody state automatically
            if (objectRB != null && canManageRigidbodyState)
            {
                // Set the settings back to how they were originally
                objectRB.useGravity = rigidBodyUseGravity;
                objectRB.isKinematic = rigidBodyisKinematic;
            }
        }

        /// <summary>
        /// Called by external scripts when the object has been picked up
        /// </summary>
        public void OnScriptPickup()
        {
            OnPickup();
        }

        /// <summary>
        /// Called by external scripts when the object has been dropped
        /// </summary>
        public void OnScriptDrop()
        {
            OnDrop();
        }

        /// <summary>
        /// Triggers PuttSync to start sending fast position updates for an amount of time (fastSyncTimeout)<br/>
        /// Having this slight delay for stopping lets the sync catch up and show where the object came to stop
        /// </summary>
        public void RequestFastSync()
        {
            currentFastSyncTimeout = fastSyncTimeout;
        }

        /// <summary>
        /// Resets the position of this object to it's original position/rotation and sends a sync (Only the owner can perform this!)
        /// </summary>
        public void Respawn()
        {
            if (Networking.LocalPlayer == null || !Networking.LocalPlayer.IsValid() || !Networking.LocalPlayer.IsOwner(gameObject))
                return;

            timeSincePlayerDroppedObject = -1f;

            // Tell player to drop the object if they're holding it
            if (pickup != null)
                pickup.Drop();

            // Freeze this object at it's spawn point
            if (objectRB != null)
            {
                objectRB.velocity = Vector3.zero;
                objectRB.angularVelocity = Vector3.zero;
                if (canManageRigidbodyState)
                {
                    objectRB.useGravity = false;
                    objectRB.isKinematic = true;
                }
            }

            this.transform.position = originalPosition;
            this.transform.rotation = originalRotation;

            RequestFastSync();
        }

        /// <summary>
        /// Sets the respawn position/rotation for this object and tells all other clients about the change (Only the owner can perform this!)
        /// </summary>
        /// <param name="position">The new spawn position in world space</param>
        /// <param name="rotation">The new spawn rotation for this object</param>
        public void SetSpawnPosition(Vector3 position, Quaternion rotation)
        {
            if (Networking.LocalPlayer == null || !Networking.LocalPlayer.IsValid() || !Networking.LocalPlayer.IsOwner(gameObject))
                return;

            originalPosition = position;
            originalRotation = rotation;

            RequestFastSync();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            fastSyncInterval = fastSyncIntervalCurve.Evaluate(VRCPlayerApi.GetPlayerCount());
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            fastSyncInterval = fastSyncIntervalCurve.Evaluate(VRCPlayerApi.GetPlayerCount());
        }
    }
}
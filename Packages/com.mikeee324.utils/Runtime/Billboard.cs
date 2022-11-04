
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace mikeee324.Utils
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class Billboard : UdonSharpBehaviour
    {
        public GameObject attachToObject;

        void Start()
        {
        }

        void Update()
        {
            if (!Networking.LocalPlayer.IsValid())
                return;

            transform.LookAt(Networking.LocalPlayer.GetBonePosition(HumanBodyBones.Head));

            if (attachToObject != null)
                transform.position = attachToObject.transform.position + new Vector3(0, 0.1f, 0);
        }
    }
}
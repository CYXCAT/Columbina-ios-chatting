using UnityEngine;

namespace Companion
{
    // Keeps the character from sinking through the floor: after the Animator has posed the body,
    // if a foot, toe or the hips end up below the ground plane (this transform's height) the whole
    // body is pushed up by the penetration. Runs before the spring bones (VRMSpringBone, order 11000)
    // so hair and cloth follow the corrected pose.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(Animator))]
    public class GroundContact : MonoBehaviour
    {
        [Tooltip("Distance from the foot/toe bones to the sole.")]
        [SerializeField] float soleOffset = 0.025f;
        [Tooltip("Distance from the hips bone to the bottom of the pelvis.")]
        [SerializeField] float hipsOffset = 0.1f;

        Animator animator;
        Transform hips;
        Transform[] feet;

        void Awake()
        {
            animator = GetComponent<Animator>();
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            feet = new[]
            {
                animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                animator.GetBoneTransform(HumanBodyBones.RightFoot),
                animator.GetBoneTransform(HumanBodyBones.LeftToes),
                animator.GetBoneTransform(HumanBodyBones.RightToes),
            };
        }

        void LateUpdate()
        {
            if (hips == null) return;
            float ground = transform.position.y;
            float penetration = ground + hipsOffset - hips.position.y;
            foreach (var f in feet)
                if (f != null) penetration = Mathf.Max(penetration, ground + soleOffset - f.position.y);
            if (penetration > 0) hips.position += Vector3.up * penetration;
        }
    }
}

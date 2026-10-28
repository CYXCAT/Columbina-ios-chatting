using System.Collections.Generic;
using UniGLTF.SpringBoneJobs;
using UniGLTF.SpringBoneJobs.Blittables;
using UniGLTF.SpringBoneJobs.InputPorts;
using UnityEngine;
using VRM;

namespace Companion
{
    // Runs the character's hair/cloth on the VRM-1.0 spring bone solver (UniGLTF FastSpringBone), which
    // supports plane colliders, so the skirt, cape and sleeves rest on the floor instead of passing
    // through it. The VRMSpringBone (0.x) components stay as the authoring data — stiffness, drag,
    // gravity, hit radius, colliders — and are disabled at runtime.
    [DefaultExecutionOrder(11000)] // after the Animator and GroundContact, same slot as VRMSpringBone
    public class SpringBonePhysics : MonoBehaviour
    {
        [Tooltip("Floor plane: this transform's position, facing its up axis. Defaults to the character root.")]
        [SerializeField] Transform ground;
        [Tooltip("Raises the collision plane above the floor; bones are pushed out by their hit radius but can dip briefly.")]
        [SerializeField] float groundOffset = 0.02f;
        [Tooltip("Length of the virtual tail added after each chain's last bone (VRM 0.x uses 7 cm).")]
        [SerializeField] float tailLength = 0.07f;

        FastSpringBoneBufferCombiner combiner;
        FastSpringBoneScheduler scheduler;
        FastSpringBoneBuffer buffer;

        void Awake()
        {
            if (ground == null) ground = transform;
            var groundCollider = new FastSpringBoneCollider
            {
                Transform = ground,
                Collider = new BlittableCollider(offset: Vector3.up * groundOffset, tailOrNormal: Vector3.up,
                                                 colliderType: BlittableColliderType.Plane),
            };

            var springs = new List<FastSpringBoneSpring>();
            foreach (var source in GetComponentsInChildren<VRMSpringBone>())
            {
                source.enabled = false;
                var colliders = new List<FastSpringBoneCollider> { groundCollider };
                if (source.ColliderGroups != null)
                    foreach (var group in source.ColliderGroups)
                    {
                        if (group == null) continue;
                        foreach (var c in group.Colliders)
                            colliders.Add(new FastSpringBoneCollider
                            {
                                Transform = group.transform,
                                Collider = new BlittableCollider(c.Offset, c.Radius, colliderType: BlittableColliderType.Sphere),
                            });
                    }

                var joint = new BlittableJointMutable(source.m_stiffnessForce, source.m_gravityPower, source.m_gravityDir,
                                                      source.m_dragForce, source.m_hitRadius);
                foreach (var root in source.RootBones)
                    if (root != null)
                        AddChains(root, new List<Transform>(), joint, source.m_center, colliders.ToArray(), springs);
            }

            buffer = new FastSpringBoneBuffer(transform, springs.ToArray());
            combiner = new FastSpringBoneBufferCombiner();
            combiner.Register(add: buffer, remove: null);
            scheduler = new FastSpringBoneScheduler(combiner);
        }

        // VRM 1.0 springs are linear chains: the first child continues the chain, every other child
        // starts a new one, and each leaf gets a virtual tail (the last joint of a chain is not simulated).
        void AddChains(Transform head, List<Transform> chain, BlittableJointMutable joint, Transform center,
                       FastSpringBoneCollider[] colliders, List<FastSpringBoneSpring> springs)
        {
            chain.Add(head);
            if (head.childCount == 0)
            {
                chain.Add(CreateTail(head));
                var joints = new FastSpringBoneJoint[chain.Count];
                for (int i = 0; i < chain.Count; i++)
                    joints[i] = new FastSpringBoneJoint { Transform = chain[i], Joint = joint, DefaultLocalRotation = chain[i].localRotation };
                springs.Add(new FastSpringBoneSpring { center = center, joints = joints, colliders = colliders });
                return;
            }
            AddChains(head.GetChild(0), chain, joint, center, colliders, springs);
            for (int i = 1; i < head.childCount; i++)
                AddChains(head.GetChild(i), new List<Transform>(), joint, center, colliders, springs);
        }

        Transform CreateTail(Transform leaf)
        {
            var tail = new GameObject(leaf.name + "_tail").transform;
            tail.SetParent(leaf, false);
            var dir = leaf.parent != null ? (leaf.position - leaf.parent.position).normalized : Vector3.down;
            tail.position = leaf.position + dir * tailLength;
            return tail;
        }

        void LateUpdate()
        {
            scheduler?.Schedule(Time.deltaTime).Complete();
        }

        void OnDestroy()
        {
            if (combiner != null && buffer != null) combiner.Register(add: null, remove: buffer);
            scheduler?.Dispose(); // also disposes the combiner
            buffer?.Dispose();
        }
    }
}

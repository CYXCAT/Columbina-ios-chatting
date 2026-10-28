using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Companion.EditorTools
{
    // Builds the character's motion assets from the KAWAII animation pack:
    //  - Rest: KA_Speak01_Normal_Loop with arm/hand and sideways/twist muscles frozen at frame 0 (breathing only).
    //  - Speak: KA_Speak04_Calm Start → Loop → End, driven by the Speaking bool.
    //  - Idle 1..4: Start → Loop → End variants entered when Idle == n and left when Idle returns to 0.
    // CompanionMotion drives the parameters at runtime. Re-run from the menu after changing clips.
    public static class CompanionAnimatorBuilder
    {
        const string Pack = "Assets/KAWAII_ANIMATIOMS_100/Assets/Animations/";
        const string OutDir = "Assets/Characters/Shaonv/";
        const string RestClipPath = OutDir + "Shaonv_RestBreathing.anim";
        const string ControllerPath = OutDir + "Shaonv_Companion.controller";

        // Arms/hands, every left-right / twist channel, the body yaw and the neck/head nod, so the head
        // stays put. The torso's front-back bend is kept, scaled down, as the breathing motion.
        static readonly Regex FrozenCurve = new Regex("Shoulder|Arm|Forearm|Hand|Left-Right|Twist|RootQ|Nod");
        static readonly Regex BreathingCurve = new Regex("^(Spine|Chest|UpperChest) Front-Back$");
        const float BreathingScale = 0.35f;

        // Idle == index + 1
        static readonly (string name, string start, string loop, string end)[] Idles =
        {
            ("Sing", "Idle/Idle_Sing/@KA_Idle81_Sing_NoMicrophone_Start", "Idle/Idle_Sing/@KA_Idle81_Sing_NoMicrophone_Loop1", "Idle/Idle_Sing/@KA_Idle81_Sing_NoMicrophone_End"),
            ("SitFloor", "Idle/Idle_Sit/@KA_Idle46_SitFloor_Start", "Idle/Idle_Sit/@KA_Idle46_SitFloor_Loop", "Idle/Idle_Sit/@KA_Idle46_SitFloor_End"),
            ("SitLookAround", "Sit/TransitionFromStandingPose/@KA_Sit_Start", "Sit/TransitionFromStandingPose/@KA_Sit_LookingAround", "Sit/TransitionFromStandingPose/@KA_Sit_End"),
            ("FlyWitch", "Action/@KA_Fly_Start_Witch", "Action/@KA_Fly_Loop_Witch", "Action/@KA_Fly_End_Witch"),
        };

        const string SpeakStart = "Speak/@KA_Speak04_Calm_Start";
        const string SpeakLoop = "Speak/@KA_Speak04_Calm_Loop";
        const string SpeakEnd = "Speak/@KA_Speak04_Calm_End";
        const string RestSource = "Speak/@KA_Speak01_Normal_Loop";

        [MenuItem("Companion/Build Animator")]
        public static void Build()
        {
            ConfigureClip(RestSource, false);
            ConfigureClip(SpeakStart, false);
            ConfigureClip(SpeakLoop, true);
            ConfigureClip(SpeakEnd, false);
            foreach (var idle in Idles)
            {
                ConfigureClip(idle.start, false);
                ConfigureClip(idle.loop, true);
                ConfigureClip(idle.end, false);
            }

            var rest = BuildRestClip();
            // Rebuild in place so scene references to the controller keep working.
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                             ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (var p in controller.parameters) controller.RemoveParameter(p);
            controller.AddParameter("Speaking", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Idle", AnimatorControllerParameterType.Int);

            var sm = controller.layers[0].stateMachine;
            foreach (var s in sm.states) sm.RemoveState(s.state);
            foreach (var t in sm.anyStateTransitions) sm.RemoveAnyStateTransition(t);
            var restState = sm.AddState("Rest", new Vector3(300, 0));
            restState.motion = rest;
            sm.defaultState = restState;

            // Speak
            var sStart = AddState(sm, "Speak_Start", SpeakStart, new Vector3(600, -200));
            var sLoop = AddState(sm, "Speak_Loop", SpeakLoop, new Vector3(900, -200));
            var sEnd = AddState(sm, "Speak_End", SpeakEnd, new Vector3(600, -100));
            Transition(restState, sStart, 0.3f).AddCondition(AnimatorConditionMode.If, 0, "Speaking");
            ExitTransition(sStart, sLoop, 0.25f);
            Transition(sLoop, sEnd, 0.3f).AddCondition(AnimatorConditionMode.IfNot, 0, "Speaking");
            Transition(sEnd, sLoop, 0.3f).AddCondition(AnimatorConditionMode.If, 0, "Speaking");
            ExitTransition(sEnd, restState, 0.4f);

            // Idles
            for (int i = 0; i < Idles.Length; i++)
            {
                var idle = Idles[i];
                int n = i + 1;
                float y = 100 + i * 100;
                var start = AddState(sm, idle.name + "_Start", idle.start, new Vector3(600, y));
                var loop = AddState(sm, idle.name + "_Loop", idle.loop, new Vector3(900, y));
                var end = AddState(sm, idle.name + "_End", idle.end, new Vector3(1200, y));
                Transition(restState, start, 0.4f).AddCondition(AnimatorConditionMode.Equals, n, "Idle");
                ExitTransition(start, loop, 0.25f);
                Transition(loop, end, 0.3f).AddCondition(AnimatorConditionMode.NotEqual, n, "Idle");
                ExitTransition(end, restState, 0.4f);
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("Companion animator built: " + ControllerPath);
        }

        static AnimationClip BuildRestClip()
        {
            var src = LoadClip(RestSource);
            var clip = new AnimationClip { name = "Shaonv_RestBreathing" };
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                var curve = AnimationUtility.GetEditorCurve(src, b);
                if (FrozenCurve.IsMatch(b.propertyName))
                {
                    var v = curve.Evaluate(0);
                    curve = AnimationCurve.Constant(0, src.length, v);
                }
                else if (BreathingCurve.IsMatch(b.propertyName))
                {
                    float mean = 0;
                    foreach (var k in curve.keys) mean += k.value;
                    mean /= curve.length;
                    var keys = curve.keys;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        keys[i].value = mean + (keys[i].value - mean) * BreathingScale;
                        keys[i].inTangent *= BreathingScale;
                        keys[i].outTangent *= BreathingScale;
                    }
                    curve.keys = keys;
                }
                AnimationUtility.SetEditorCurve(clip, b, curve);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(src);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(RestClipPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                return existing;
            }
            AssetDatabase.CreateAsset(clip, RestClipPath);
            return clip;
        }

        static AnimatorState AddState(AnimatorStateMachine sm, string name, string clip, Vector3 pos)
        {
            var s = sm.AddState(name, pos);
            s.motion = LoadClip(clip);
            return s;
        }

        static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = duration;
            return t;
        }

        // Plays the source clip to the end, blending into the next state during its last `duration` seconds.
        static void ExitTransition(AnimatorState from, AnimatorState to, float duration)
        {
            var t = Transition(from, to, duration);
            var length = ((AnimationClip)from.motion).length;
            t.hasExitTime = true;
            t.exitTime = Mathf.Clamp01((length - duration) / length);
        }

        static AnimationClip LoadClip(string file)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Pack + file + ".FBX"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
            throw new System.Exception("No clip in " + file);
        }

        // Root motion is off on the character, so vertical root motion (sitting down, standing up,
        // take-off) would be discarded and the body would float or sink. Baking the root height into
        // the pose (based on the original height) keeps the body where the clip puts it.
        static void ConfigureClip(string file, bool loop)
        {
            var path = Pack + file + ".FBX";
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            if (clips.Length == 0) return;
            var c = clips[0];
            if (c.loopTime == loop && c.lockRootHeightY && c.keepOriginalPositionY && !c.heightFromFeet) return;
            c.loopTime = loop;
            c.lockRootHeightY = true;
            c.keepOriginalPositionY = true;
            c.heightFromFeet = false;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }
}

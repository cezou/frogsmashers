using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FrogSmashers.Editor
{
    /// <summary>
    /// One-shot generator for Assets/Resources/GameAudioMixer.mixer
    /// (groups Master > Music/SFX, exposed params MusicVolume and
    /// SFXVolume). Unity has no public API to author AudioMixer
    /// assets, so this drives the internal
    /// UnityEditor.Audio.AudioMixerController via reflection; the
    /// committed asset is the durable output, this script is the
    /// regeneration path.
    /// </summary>
    public static class AudioMixerGenerator
    {
        const string AssetPath = "Assets/Resources/GameAudioMixer.mixer";

        [MenuItem("Tools/FrogSmashers/Generate Audio Mixer")]
        public static void Generate()
        {
            if (File.Exists(AssetPath))
            {
                Log($"{AssetPath} already exists; delete it first to " +
                    "regenerate.");
                return;
            }

            var asm = typeof(UnityEditor.Editor).Assembly;
            var controllerT =
                asm.GetType("UnityEditor.Audio.AudioMixerController");
            var groupT =
                asm.GetType("UnityEditor.Audio.AudioMixerGroupController");
            var effectT =
                asm.GetType("UnityEditor.Audio.AudioMixerEffectController");
            var snapshotT = asm.GetType(
                "UnityEditor.Audio.AudioMixerSnapshotController");
            var exposedT =
                asm.GetType("UnityEditor.Audio.ExposedAudioParameter");
            if (controllerT == null || groupT == null || effectT == null
                || snapshotT == null || exposedT == null)
            {
                Log("ERROR: internal AudioMixerController types not " +
                    "found; fall back to hand-authored YAML.");
                return;
            }

            var controller =
                (UnityEngine.Object)Activator.CreateInstance(
                    controllerT);
            controller.name = "GameAudioMixer";

            var snapshot = (UnityEngine.Object)Activator
                .CreateInstance(snapshotT, new object[] { controller });
            snapshot.name = "Snapshot";
            var snapshots = Array.CreateInstance(snapshotT, 1);
            snapshots.SetValue(snapshot, 0);
            Set(controller, "snapshots", snapshots);
            Set(controller, "startSnapshot", snapshot);

            var master = NewGroup(groupT, effectT, controller, "Master");
            Set(controller, "masterGroup", master);

            var music = NewGroup(groupT, effectT, controller, "Music");
            var sfx = NewGroup(groupT, effectT, controller, "SFX");
            Invoke(controller, "AddChildToParent",
                new[] { groupT, groupT }, music, master);
            Invoke(controller, "AddChildToParent",
                new[] { groupT, groupT }, sfx, master);

            Expose(controller, exposedT, music, "MusicVolume", sfx,
                "SFXVolume");

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            AssetDatabase.CreateAsset(controller, AssetPath);
            AddSub(snapshot, controller);
            AddSub(master, controller);
            AddSub(music, controller);
            AddSub(sfx, controller);
            AddGroupEffects(master, controller);
            AddGroupEffects(music, controller);
            AddGroupEffects(sfx, controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(AssetPath);
            Log($"Created {AssetPath} (Master > Music/SFX, exposed " +
                "MusicVolume + SFXVolume).");
        }

        static UnityEngine.Object NewGroup(Type groupT, Type effectT,
            object controller, string name)
        {
            var group = (UnityEngine.Object)Activator.CreateInstance(
                groupT, new object[] { controller });
            group.name = name;
            var effect = (UnityEngine.Object)Activator.CreateInstance(
                effectT, new object[] { "Attenuation" });
            Invoke(group, "InsertEffect",
                new[] { effectT, typeof(int) }, effect, 0);
            Invoke(group, "PreallocateGUIDs", Type.EmptyTypes);
            return group;
        }

        static void Expose(object controller, Type exposedT,
            object musicGroup, string musicName, object sfxGroup,
            string sfxName)
        {
            var music = NewExposed(exposedT, musicGroup, musicName);
            var sfx = NewExposed(exposedT, sfxGroup, sfxName);
            var list = Array.CreateInstance(exposedT, 2);
            list.SetValue(music, 0);
            list.SetValue(sfx, 1);
            Set(controller, "exposedParameters", list);
        }

        static object NewExposed(Type exposedT, object group,
            string name)
        {
            var guid = Invoke(group, "GetGUIDForVolume",
                Type.EmptyTypes);
            var exposed = Activator.CreateInstance(exposedT);
            SetField(exposed, "guid", guid);
            SetField(exposed, "name", name);
            return exposed;
        }

        static void AddGroupEffects(object group, UnityEngine.Object
            asset)
        {
            var effects = Get(group, "effects") as Array;
            if (effects == null)
                return;
            foreach (var effect in effects)
                AddSub(effect as UnityEngine.Object, asset);
        }

        static void AddSub(UnityEngine.Object sub,
            UnityEngine.Object asset)
        {
            if (sub != null)
                AssetDatabase.AddObjectToAsset(sub, asset);
        }

        const BindingFlags Flags = BindingFlags.Public
            | BindingFlags.NonPublic | BindingFlags.Instance;

        static void Set(object target, string name, object value)
        {
            var prop = target.GetType().GetProperty(name, Flags);
            if (prop != null)
            {
                prop.SetValue(target, value);
                return;
            }
            var field = target.GetType().GetField(name, Flags);
            if (field == null)
                throw new MissingMemberException(
                    target.GetType().Name, name);
            field.SetValue(target, value);
        }

        static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, Flags);
            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }
            Set(target, name, value);
        }

        static object Get(object target, string name)
        {
            var prop = target.GetType().GetProperty(name, Flags);
            if (prop != null)
                return prop.GetValue(target);
            var field = target.GetType().GetField(name, Flags);
            if (field == null)
                throw new MissingMemberException(
                    target.GetType().Name, name);
            return field.GetValue(target);
        }

        static object Invoke(object target, string name,
            Type[] signature, params object[] args)
        {
            var method = target.GetType().GetMethod(name, Flags, null,
                signature, null);
            if (method == null)
                throw new MissingMethodException(
                    target.GetType().Name, name);
            return method.Invoke(target, args);
        }

        static void Log(string msg)
        {
            Debug.Log($"[AudioMixerGenerator] {msg}");
        }
    }
}

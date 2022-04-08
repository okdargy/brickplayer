using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager instance;

    private Dictionary<string, AudioClip> audioDictionary = new Dictionary<string, AudioClip>();

    private void Awake () {
        if (instance == null) {
            instance = this;
        } else {
            Destroy(this);
        }
    }

    public static void AddClip (AudioClip clip) {
        instance.audioDictionary.Add(clip.name, clip);
    }

    public static void PlaySoundPacket (PacketReader r) {
        string clipName = r.ReadString();
        float clipVolume = r.ReadFloat();
        float clipPitch = r.ReadFloat();
        byte audioDims = r.ReadByte();

        if (audioDims == 0) {
            // 2D
            PlaySound(clipName, clipVolume, clipPitch);
        } else {
            // 3D
            float x = r.ReadFloat();
            float y = r.ReadFloat();
            float z = r.ReadFloat();
            PlaySound3D(clipName, new Vector3(-x,z,y), clipVolume, clipPitch);
        }
    }

    public static void PlaySound (string name, float volume = 1.0f, float pitch = 1.0f) {
        if (instance.audioDictionary.TryGetValue(name, out AudioClip clip)) {
            GameObject sfx = new GameObject("Sound Effect - " + name);
            AudioSource src = sfx.AddComponent<AudioSource>();
            src.volume = volume;
            src.pitch = pitch;
            src.spatialBlend = 0f; // 2d
            src.clip = instance.audioDictionary[name];
            
            src.Play();
            Destroy(sfx, clip.length / pitch);
        }
    }

    public static void PlaySound3D (string name, Vector3 position, float volume = 1.0f, float pitch = 1.0f) {
        if (instance.audioDictionary.TryGetValue(name, out AudioClip clip)) {
            GameObject sfx = new GameObject("Sound Effect - " + name);
            sfx.transform.position = position;
            AudioSource src = sfx.AddComponent<AudioSource>();
            src.volume = volume;
            src.pitch = pitch;
            src.spatialBlend = 1f; // 3d
            src.clip = instance.audioDictionary[name];

            src.Play();
            Destroy(sfx, clip.length / pitch);
        }
    }

    public static void PlaySound (AudioClip clip, float volume = 1.0f, float pitch = 1.0f) {
        GameObject sfx = new GameObject("Sound Effect - " + clip.name);
        AudioSource src = sfx.AddComponent<AudioSource>();
        src.volume = volume;
        src.pitch = pitch;
        src.spatialBlend = 0f; // 2d
        src.clip = clip;
        
        src.Play();
        Destroy(sfx, clip.length / pitch);
    }

    public static void PlaySound3D (AudioClip clip, Vector3 position, float volume = 1.0f, float pitch = 1.0f) {
        GameObject sfx = new GameObject("Sound Effect - " + clip.name);
        sfx.transform.position = position;
        AudioSource src = sfx.AddComponent<AudioSource>();
        src.volume = volume;
        src.pitch = pitch;
        src.spatialBlend = 1f; // 3d
        src.clip = clip;

        src.Play();
        Destroy(sfx, clip.length / pitch);
    }
}

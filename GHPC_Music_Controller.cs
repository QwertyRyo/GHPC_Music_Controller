using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using MelonLoader;
using GHPC;
using GHPC.Audio;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;

namespace GHPC_Music_Controller {
  public class GHPC_Music_ControllerClass : MelonMod {
    public static bool activeScene = true;
    public static MelonPreferences_Entry<string> song_choice;

    private GameObject gameManager;

    private static readonly
        Dictionary<string, DynamicMusicController.MusicTrack> _trackCache =
            new Dictionary<string, DynamicMusicController.MusicTrack>();

    private static readonly HashSet<string> _inGameSongs =
        new HashSet<string> { "Threat Factor", "Defilade", "Steel Horizon",
                              "Danger Close" };

    private static readonly HashSet<string> _bundledOverrideSongs =
        new HashSet<string> { "Broken Arrow", "Not One Step Back", "Zero Hour"};

    private static string AssetsFolder => Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        "Music_Controller_Assets");

    private static string LegacyAssetsFolder => "Mods/Music_Controller_Assets";

    public override void OnInitializeMelon() {
      MelonLogger.Msg("Music Controller Initialized");

      MelonPreferences_Category cfg =
          MelonPreferences.CreateCategory("GHPC_Music_Controller");
      song_choice = cfg.CreateEntry<string>("Song Choice", "");
      song_choice.Comment =
          "Pick between the following in-game songs: \"Broken Arrow\", \"Not One Step Back\", \"Defilade\", \"Threat Factor\", \"Danger Close\", \"Steel Horizon\", \"Zero Hour\", or your own custom song name. Custom songs need 4 audio files (.wav or .ogg) as layers in Music_Controller_Assets/ named YourSong_L1.wav, YourSong_L2.wav, YourSong_L3.wav, YourSong_L4.wav. _L1 = low intensity, _L2 = medium, _L3 = high, _L4 = max intensity. All files for 1 song must have same length, sample rate, channels, and bit depth. Leave blank for default random behavior. Works with OGG-Vorpis or 16-bit pcm wav formats.";

      MelonPreferences.Save();
      HarmonyInstance.PatchAll();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName) {
      if (sceneName == "MainMenu2_Scene" || sceneName == "t64_menu" ||
          sceneName == "MainMenu2-1_Scene" || sceneName == "leo2_menu") {
        activeScene = false;
        return;
      }

      activeScene = true;
      gameManager = GameObject.Find("_APP_GHPC_");
    }

    public static bool IsInGameSong(string name) =>
        !string.IsNullOrEmpty(name) && _inGameSongs.Contains(name);

    public static bool IsBundledOverride(string name) =>
        !string.IsNullOrEmpty(name) && _bundledOverrideSongs.Contains(name);

    public static bool IsCustomSong(string name) =>
        !string.IsNullOrEmpty(name) && !_inGameSongs.Contains(name) &&
        !_bundledOverrideSongs.Contains(name);

    public static bool NeedsOverrideInjection(string name) =>
        IsBundledOverride(name) || IsCustomSong(name);

    public static int GetTrackIndex(int min, int max) {
      string choice = song_choice?.Value?.Trim();
      switch (choice) {
        case "Threat Factor":
          return 0;
        case "Defilade":
          return 1;
        case "Steel Horizon":
          return 2;
        case "Danger Close":
          return 3;
      }
      return UnityEngine.Random.Range(min, max);
    }

    public static DynamicMusicOverride InterceptOverrideLookup()
        {
    string choice = song_choice?.Value?.Trim();
    if (!string.IsNullOrEmpty(choice))
    {
        //MelonLogger.Msg("[Music] Suppressed DynamicMusicOverride lookup");
        return null;
    }

    return Object.FindObjectOfType<DynamicMusicOverride>();
    }


    public static DynamicMusicController.MusicTrack GetOrBuildTrack(
        string name) {
      if (_trackCache.TryGetValue(name, out var cached) && cached != null &&
          cached.Layer1 != null)
        return cached;

      // Determine folder
      string folder;
      if (IsBundledOverride(name))
        folder = AssetsFolder;
      else
        folder = Directory.Exists(Path.Combine(AssetsFolder, ".."))
                     ? AssetsFolder
                     : LegacyAssetsFolder;

      AudioClip l1 = null, l2 = null, l3 = null, l4 = null;
      l1 = LoadAudioFile(folder, name + "_L1");
      l2 = LoadAudioFile(folder, name + "_L2");
      l3 = LoadAudioFile(folder, name + "_L3");
      l4 = LoadAudioFile(folder, name + "_L4");

      if (l1 == null || l2 == null || l3 == null || l4 == null) {
        MelonLogger.Warning(
            $"[Music] Could not load all 4 layers for '{name}' from {folder}");
        return null;
      }

      l1.hideFlags |= HideFlags.DontUnloadUnusedAsset;
      l2.hideFlags |= HideFlags.DontUnloadUnusedAsset;
      l3.hideFlags |= HideFlags.DontUnloadUnusedAsset;
      l4.hideFlags |= HideFlags.DontUnloadUnusedAsset;

      var track = new DynamicMusicController.MusicTrack {
        Name = name,  Layer1 = l1,  Layer2 = l2,  Layer3 = l3, Layer4 = l4,
        Volume1 = 1f, Volume2 = 1f, Volume3 = 1f, Volume4 = 1f
      };

      _trackCache[name] = track;
      MelonLogger.Msg($"[Music] Built MusicTrack '{name}'");
      return track;
    }

    private static AudioClip LoadAudioFile(string folder, string baseName) {
      string oggPath = Path.Combine(folder, baseName + ".ogg");
      if (File.Exists(oggPath))
        return LoadWav(oggPath);

      string wavPath = Path.Combine(folder, baseName + ".wav");
      if (File.Exists(wavPath))
        return LoadWav(wavPath);

      MelonLogger.Warning(
          $"[Music] Audio not found: {baseName} (.ogg or .wav) in {folder}");
      return null;
    }

    private static AudioClip LoadWav(string path) {
      string ext = Path.GetExtension(path).ToLowerInvariant();

      if (ext == ".ogg")
        return LoadOgg(path);

      if (!File.Exists(path)) {
        MelonLogger.Warning($"[Music] WAV not found: {path}");
        return null;
      }

      byte[] data = File.ReadAllBytes(path);
      int channels = System.BitConverter.ToInt16(data, 22);
      int sampleRate = System.BitConverter.ToInt32(data, 24);
      int bitsPerSample = System.BitConverter.ToInt16(data, 34);

      int pos = 12;
      while (pos < data.Length - 8) {
        int chunkSize = System.BitConverter.ToInt32(data, pos + 4);
        if (data[pos] == 'd' && data[pos + 1] == 'a' && data[pos + 2] == 't' &&
            data[pos + 3] == 'a') {
          pos += 8;
          break;
        }
        pos += 8 + chunkSize;
      }

      if (bitsPerSample != 16) {
        MelonLogger.Warning(
            $"[Music] Unsupported bit depth {bitsPerSample} in {path} (need 16-bit PCM)");
        return null;
      }

      int sampleCount = (data.Length - pos) / 2;
      float[] samples = new float[sampleCount];
      for (int i = 0; i < sampleCount; i++) {
        short s = System.BitConverter.ToInt16(data, pos + i * 2);
        samples[i] = s / 32768f;
      }

      int frameCount = sampleCount / channels;
      AudioClip clip =
          AudioClip.Create(Path.GetFileNameWithoutExtension(path), frameCount,
                           channels, sampleRate, false);
      clip.SetData(samples, 0);
      return clip;
    }

    private static AudioClip LoadOgg(string path) {
      string url = "file:///" + path.Replace('\\', '/');
      var handler = new DownloadHandlerAudioClip(url, AudioType.OGGVORBIS);
      handler.streamAudio = false;
      handler.compressed = false;
      var req =
          new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET, handler, null);
      var op = req.SendWebRequest();

      while (!op.isDone) System.Threading.Thread.Sleep(10);

      if (req.result != UnityWebRequest.Result.Success) {
        MelonLogger.Warning($"[Music] OGG load failed: {path} — {req.error}");
        req.Dispose();
        return null;
      }

      var clip = handler.audioClip;
      if (clip == null || clip.samples == 0) {
        MelonLogger.Warning(
            $"[Music] OGG decoded to empty clip: {path} (samples={clip?.samples ?? 0})");
        req.Dispose();
        return null;
      }
      clip.name = Path.GetFileNameWithoutExtension(path);
      req.Dispose();
      return clip;
    }

[HarmonyPatch]
public class DynamicMusicPatch
{
    static MethodBase TargetMethod()
    {
        var parentType = AccessTools.TypeByName("DynamicMusicController");
        var nestedTypes = parentType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var nested in nestedTypes)
        {
            if (nested.Name.Contains("Initialize"))
                return AccessTools.Method(nested, "MoveNext");
        }
        return null;
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
{
    var randomRange = AccessTools.Method(typeof(UnityEngine.Random),
        nameof(UnityEngine.Random.Range), new[] { typeof(int), typeof(int) });
    var getTrackIndex = AccessTools.Method(typeof(GHPC_Music_ControllerClass),
        nameof(GHPC_Music_ControllerClass.GetTrackIndex));

    var findObjectOfType = AccessTools.Method(
        typeof(Object), "FindObjectOfType", System.Type.EmptyTypes)
        .MakeGenericMethod(typeof(DynamicMusicOverride));
    var interceptOverride = AccessTools.Method(typeof(GHPC_Music_ControllerClass),
        nameof(GHPC_Music_ControllerClass.InterceptOverrideLookup));

    var codes = new List<CodeInstruction>(instructions);
    for (int i = 0; i < codes.Count; i++)
    {
        if (codes[i].Calls(randomRange))
        {
            var replacement = new CodeInstruction(OpCodes.Call, getTrackIndex);
            replacement.labels = codes[i].labels;       // preserve labels
            replacement.blocks = codes[i].blocks;       // preserve exception blocks
            codes[i] = replacement;
           // MelonLogger.Msg("[Music] Transpiler: patched Random.Range -> GetTrackIndex");
        }
        else if (codes[i].Calls(findObjectOfType))
        {
            var replacement = new CodeInstruction(OpCodes.Call, interceptOverride);
            replacement.labels = codes[i].labels;
            replacement.blocks = codes[i].blocks;
            codes[i] = replacement;
       //     MelonLogger.Msg("[Music] Transpiler: patched FindObjectOfType -> InterceptOverrideLookup");
        }
    }
    return codes;
}
}

   [HarmonyPatch(typeof(DynamicMusicController), "StartMusic")]
    public static class Patch_DynamicMusicController_StartMusic {
      [HarmonyPrefix]
      public static void Prefix(DynamicMusicController __instance) {
        if (!activeScene)
          return;

        string choice = song_choice?.Value?.Trim();
        if (string.IsNullOrEmpty(choice))
          return;

        if (IsInGameSong(choice))
          return;

        var track = GetOrBuildTrack(choice);
        if (track == null)
          return;

        var t = Traverse.Create(__instance);

        t.Field("_activeTrack").SetValue(track);

        var source1 = t.Field("_source1").GetValue<AudioSource>();
        if (source1 != null)
          source1.clip = track.Layer1;

        var source2 = t.Field("_source2").GetValue<AudioSource>();
        if (source2 != null)
          source2.clip = track.Layer1;

        t.Field("_source1LayerVolume").SetValue(track.Volume1);
        t.Field("_source2LayerVolume").SetValue(track.Volume1);
        t.Field("_currentLayerVolume").SetValue(track.Volume1);
        t.Field("_goalLayerVolume").SetValue(track.Volume1);

        t.Field("_currentLayer").SetValue(1);
        t.Field("_source1IsMain").SetValue(true);
        t.Field("_source1Ratio").SetValue(1f);
        t.Field("_source2Ratio").SetValue(0f);
        t.Field("_fadingNow").SetValue(false);

        MelonLogger.Msg($"[Music] Forced _activeTrack = '{track.Name}'");
      }

      [HarmonyPostfix]
      public static void Postfix(DynamicMusicController __instance) {
        if (!activeScene)
          return;
        var activeTrack = Traverse.Create(__instance)
                              .Field("_activeTrack")
                              .GetValue<DynamicMusicController.MusicTrack>();
        MelonLogger.Msg($"[Music] Now playing: {activeTrack?.Name ?? "null"}");
      }
    }

    [HarmonyPatch(typeof(DynamicMusicController), "Awake")]
    public static class Patch_DynamicMusicController_Awake {
      [HarmonyPrefix]
      public static void Prefix(DynamicMusicController __instance) {
        if (!activeScene)
          return;

        string choice = song_choice?.Value?.Trim();
        if (string.IsNullOrEmpty(choice))
          return;

        Traverse.Create(__instance)
            .Field("_playOnlyInDynamicMissions")
            .SetValue(false);

        var dmo = Object.FindObjectOfType<DynamicMusicOverride>();
        if (dmo != null) {
          Object.Destroy(dmo.gameObject);
    //      MelonLogger.Msg("[Music] Destroyed scene's DynamicMusicOverride");
        }
      }
    }

    [HarmonyPatch(typeof(DynamicMusicController), "CrossfadeToLayer")]
    public static class Patch_DynamicMusicController_CrossfadeToLayer {
      [HarmonyPostfix]
      public static void Postfix(int layerNumber) {
        if (!activeScene)
          return;
        /*MelonLogger.Msg($"[Music] Layer -> {layerNumber}");*/
      }
    }
  }
}

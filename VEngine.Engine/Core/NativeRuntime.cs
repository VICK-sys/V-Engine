using System.Runtime.InteropServices;

namespace VEngine.Engine.Core;

public static class NativeRuntime
{
    private static readonly Dictionary<string, string[]> Exports = new()
    {
        ["audio_mixer"] = ["mixer_create", "mixer_mix"],
        ["batch_sorter"] = ["batch_create", "batch_sort_and_cull"],
        ["fluid_solver"] = ["fluid_create", "fluid_substep"],
        ["gif_decoder"] = ["gif_open", "gif_frame_data"],
        ["image_process"] = ["imgproc_blur", "imgproc_invert"],
        ["noise"] = ["noise_seed", "noise_perlin_2d"],
        ["pathfinder"] = ["pf_create", "pf_find_path"],
        ["physics_solver"] = ["physics_contact_presolve", "physics_contact_velocity", "physics_contact_position"],
        ["tilemap_collision"] = ["tilecol_create", "tilecol_raycast"],
        ["video_player"] = ["video_open", "video_next_frame"],
        ["SDL2"] = ["SDL_Init"],
        ["SDL2_ttf"] = ["TTF_Init"],
        ["SDL2_mixer"] = ["Mix_OpenAudio"]
    };

    private static readonly Dictionary<string, bool> Available = new();

    public static bool IsAvailable(string library)
    {
        if (!Exports.TryGetValue(library, out var exports))
            throw new ArgumentException("Unknown runtime library.", nameof(library));
        lock (Available)
        {
            if (Available.TryGetValue(library, out var available)) return available;
            available = NativeLibrary.TryLoad(library, typeof(NativeRuntime).Assembly, null, out var handle);
            if (available)
            {
                try { available = exports.All(export => NativeLibrary.TryGetExport(handle, export, out _)); }
                finally { NativeLibrary.Free(handle); }
            }
            Available[library] = available;
            return available;
        }
    }

    public static IReadOnlyDictionary<string, bool> Inspect() =>
        Exports.Keys.ToDictionary(name => name, IsAvailable);

    public static void Require(string library)
    {
        if (!IsAvailable(library))
            throw new DllNotFoundException($"Native runtime '{library}' is missing or incompatible. Install the VEngine.Engine runtime package for this platform.");
    }
}

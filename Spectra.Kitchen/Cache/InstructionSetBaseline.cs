using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace Spectra.Kitchen.Cache;

/// <summary>
/// The vector instruction-set baseline this cook is running under, as one stable
/// token that goes into every cache key.
/// </summary>
// The BC7 encoder gives different bytes with and without AVX2 (310 of 1,024
// blocks in the measured case), with nothing in the output to show it. A JIT
// host and a default AOT publish differ this way on one machine, so a cache
// keyed without this would hand one the other's artifact.
// The probe reports what the process supports. Under AOT that can be more than
// the compiled code uses, so a fixed IlcInstructionSet build should set Pinned.
public static class InstructionSetBaseline
{
    private static string? _pinned;
    private static string? _probed;

    /// <summary>
    /// The baseline this binary was compiled for, set at startup by a host
    /// published with a fixed <c>IlcInstructionSet</c>. Null means probe.
    /// </summary>
    public static string? Pinned
    {
        get => _pinned;
        set => _pinned = value;
    }

    /// <summary>
    /// The token the cache key carries: the pin if one was declared, else a probe
    /// of this process.
    /// </summary>
    // No lock: a race computes the same string twice.
    public static string Token => _pinned ?? (_probed ??= Probe());

    private static string Probe()
    {
        var token = new StringBuilder(96);

        // JIT adapts to the host CPU, a default AOT publish does not.
        token.Append(RuntimeFeature.IsDynamicCodeCompiled ? "jit" : "aot");
        token.Append(';');
        token.Append(DescribeArchitecture(RuntimeInformation.ProcessArchitecture));

        token.Append(";vt");
        token.Append(System.Numerics.Vector<byte>.Count.ToString(CultureInfo.InvariantCulture));
        token.Append(';');

        // Append only, and every flag is written with its value. Removing one
        // merges two baselines into one cache identity.
        Flag(token, "sse42", Sse42.IsSupported, first: true);
        Flag(token, "avx", Avx.IsSupported);
        Flag(token, "avx2", Avx2.IsSupported);
        Flag(token, "fma", Fma.IsSupported);
        Flag(token, "avx512f", Avx512F.IsSupported);
        Flag(token, "advsimd", AdvSimd.IsSupported);

        return token.ToString();
    }

    private static void Flag(StringBuilder into, string name, bool supported, bool first = false)
    {
        if (!first) into.Append(',');
        into.Append(name);
        into.Append('=');
        into.Append(supported ? '1' : '0');
    }

    // Not ToString(): enum names need metadata that trimming removes.
    private static string DescribeArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X86 => "x86",
        Architecture.X64 => "x64",
        Architecture.Arm => "arm",
        Architecture.Arm64 => "arm64",
        Architecture.Wasm => "wasm",
        Architecture.S390x => "s390x",
        Architecture.LoongArch64 => "loongarch64",
        Architecture.Armv6 => "armv6",
        Architecture.Ppc64le => "ppc64le",
        Architecture.RiscV64 => "riscv64",

        _ => "arch" + ((int)architecture).ToString(CultureInfo.InvariantCulture),
    };
}

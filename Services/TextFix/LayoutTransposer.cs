using System;
using System.Collections.Generic;
using System.Text;
using OopsType.Native;

namespace OopsType.Services.TextFix;

/// <summary>
/// Re-types text from one keyboard layout onto another: given the characters a user produced while
/// the wrong layout was active, work out what the SAME physical keystrokes would have produced under
/// the layout they meant to use. "akuo guko" typed on US English becomes "שלום עולם" on Hebrew.
///
/// <para><b>No per-language tables, ever.</b> The map is derived at runtime from Windows itself, by
/// asking both layouts what each physical key produces and joining on that key. Install a Russian,
/// Greek or French layout and it works the same day, with no code change — which is the whole reason
/// this class exists instead of a hand-written he-to-en dictionary.</para>
///
/// <para><b>Why scan codes, not virtual keys.</b> The join key has to be the PHYSICAL key, and the
/// VK-to-key relationship is itself layout-dependent: on a French AZERTY the key that reports VK_A
/// sits where a US QWERTY reports VK_Q. Joining on VK would silently produce nonsense for every
/// non-QWERTY pair. Scan codes are hardware-level and identical across layouts, so we walk scan codes
/// and let <c>MapVirtualKeyEx</c> resolve each layout's own VK for that key.</para>
///
/// <para><b>Threading.</b> <c>ToUnicodeEx</c> mutates PER-THREAD dead-key state. Callers should build
/// maps on a worker thread, never on a thread that is also processing the user's real keystrokes;
/// every entry point here flushes that state afterwards regardless (see <see cref="FlushDeadKeyState"/>).
/// The cache is guarded by a plain lock — builds are rare (once per layout pair) and cheap. It is
/// never invalidated and does not need to be: an HKL identifies a specific layout, so installing a
/// new one simply produces a cache miss under a new key.</para>
/// </summary>
public static class LayoutTransposer
{
    // Scan codes of the main typing block: the digit row, the three letter rows, and the extra key
    // 102-key boards add at 0x56. Everything outside this range is a navigation/function/modifier
    // key that produces no text.
    private const uint FirstScanCode = 0x02;
    private const uint LastScanCode = 0x56;

    // The modifier combinations worth mapping. AltGr (Ctrl+Alt) carries real letters on Polish,
    // Croatian and several other layouts, so it earns its place even though he/en never use it.
    private static readonly byte[][] ShiftStates = BuildShiftStates();

    // For each shift state, the state to fall back to on the TARGET layout when the shifted position
    // there is not really part of that layout's script: shifted falls back to unshifted, -1 means
    // "no fallback". This is what makes capitals work against a case-less script — see BuildMap.
    private static readonly int[] ShiftStateFallback = { -1, 0, -1, 2 };

    private static readonly object CacheLock = new();
    private static readonly Dictionary<(IntPtr Src, IntPtr Dst), Dictionary<char, char>> MapCache = new();
    private static readonly Dictionary<IntPtr, TextScript> ScriptCache = new();

    /// <summary>
    /// Transposes <paramref name="text"/> from <paramref name="src"/> to <paramref name="dst"/>.
    /// Characters with no counterpart on the target layout pass through unchanged.
    ///
    /// <para>Note that "no counterpart" is narrower than it sounds: punctuation is NOT generally
    /// preserved, because the punctuation keys carry letters on other layouts (on Hebrew the comma
    /// key is a letter, so "," becomes one). What does survive is anything the two layouts agree on,
    /// which in practice means digits and spaces. Capitals also pass through when the target script
    /// is case-less — callers must not assume the result is single-script; see the output gate in
    /// <see cref="ConvertSelectionService"/>.</para>
    /// </summary>
    public static string Transpose(string text, IntPtr src, IntPtr dst)
    {
        if (string.IsNullOrEmpty(text) || src == dst) return text;

        var map = GetMap(src, dst);
        if (map.Count == 0) return text;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(map.TryGetValue(c, out var mapped) ? mapped : c);
        return sb.ToString();
    }

    /// <summary>
    /// True when transposing the result back to the source lands on the original again — i.e. the
    /// conversion is reversible for this specific text.
    ///
    /// <para>This is the guarantee that lets the feature ship without an undo stack: the user's way
    /// back is to select the result and switch language again, and that only actually works if the
    /// round trip returns what was there. Layout pairs are not always bijective (two source keys can
    /// collide on one target character), so rather than hope, we check — and decline the conversion
    /// entirely when the check fails, leaving the text untouched.</para>
    ///
    /// <para><b>Case is deliberately ignored.</b> Converting into a case-less script folds capitals
    /// away with nowhere to store them, so "Akuo guko" comes back as "akuo guko". Demanding an exact
    /// match would therefore reject every capitalised word — which is most sentences — for the sake
    /// of a single lost shift key. Letting case slide is the accepted cost of converting at all;
    /// everything else must still round-trip character for character.</para>
    /// </summary>
    public static bool IsReversible(string original, string converted, IntPtr src, IntPtr dst)
        => string.Equals(Transpose(converted, dst, src), original, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The dominant script a layout writes in, determined by asking it what its letter keys produce
    /// and classifying the result. Lets source detection work for any installed layout without a
    /// language-code lookup table.
    /// </summary>
    public static TextScript GetLayoutScript(IntPtr hkl)
    {
        lock (CacheLock)
        {
            if (ScriptCache.TryGetValue(hkl, out var cached)) return cached;
        }

        var script = ComputeLayoutScript(hkl);

        lock (CacheLock)
        {
            ScriptCache[hkl] = script;
        }
        return script;
    }

    private static Dictionary<char, char> GetMap(IntPtr src, IntPtr dst)
    {
        lock (CacheLock)
        {
            if (MapCache.TryGetValue((src, dst), out var cached)) return cached;
        }

        var map = BuildMap(src, dst);

        lock (CacheLock)
        {
            MapCache[(src, dst)] = map;
        }
        return map;
    }

    /// <summary>
    /// Walks the physical typing block and pairs up what the two layouts produce on each key in each
    /// shift state. First writer wins: a character reachable from several keys keeps its mapping from
    /// the earliest (unshifted, leftmost) one, which is the key the user most likely pressed.
    /// </summary>
    private static Dictionary<char, char> BuildMap(IntPtr src, IntPtr dst)
    {
        var map = new Dictionary<char, char>(128);
        var buffer = new StringBuilder(8);

        // Resolved before the loop (it does its own ToUnicodeEx pass) and used to tell a character
        // the target layout genuinely produces from one Windows parked in an unused shift position.
        var dstScript = GetLayoutScript(dst);

        try
        {
            for (var scan = FirstScanCode; scan <= LastScanCode; scan++)
            {
                // Each layout resolves the physical key to its OWN virtual key — the step that makes
                // AZERTY/QWERTZ pairs correct rather than scrambled.
                var vkSrc = NativeMethods.MapVirtualKeyEx(scan, NativeMethods.MAPVK_VSC_TO_VK_EX, src);
                var vkDst = NativeMethods.MapVirtualKeyEx(scan, NativeMethods.MAPVK_VSC_TO_VK_EX, dst);
                if (vkSrc == 0 || vkDst == 0) continue;

                for (var i = 0; i < ShiftStates.Length; i++)
                {
                    if (!TryTranslate(vkSrc, scan, ShiftStates[i], src, buffer, out var cSrc)) continue;

                    var haveDst = TryTranslate(vkDst, scan, ShiftStates[i], dst, buffer, out var cDst);

                    // CASE FOLDING. A script with no capitals has nowhere to put one, and Windows
                    // fills those shifted positions with the plain Latin capital — Shift on the
                    // Hebrew layout reports 'A', identical to the source. That makes the pair look
                    // like a key the two layouts already agree on, it gets skipped below, and the
                    // capital then survives a conversion untouched: "Akuo guko" would be written back
                    // as "A" followed by Hebrew. Recognising that the target's shifted character does
                    // not belong to the target's own script, and folding to its unshifted letter
                    // instead, is what makes a capitalised word convert at all. Deliberately limited
                    // to uppercase sources: on the punctuation keys the same "foreign" character is a
                    // genuine mapping ('<' really does become ',') and must be left alone.
                    var fallback = ShiftStateFallback[i];
                    if (fallback >= 0
                        && char.IsUpper(cSrc)
                        && (!haveDst || ScriptClassifier.Classify(cDst) != dstScript)
                        && TryTranslate(vkDst, scan, ShiftStates[fallback], dst, buffer, out var cFolded)
                        && ScriptClassifier.Classify(cFolded) == dstScript)
                    {
                        cDst = cFolded;
                        haveDst = true;
                    }

                    if (!haveDst) continue;
                    if (cSrc == cDst) continue;

                    map.TryAdd(cSrc, cDst);
                }
            }
        }
        finally
        {
            // Leave no dead-key residue on this thread even if a translation threw midway.
            FlushDeadKeyState(src);
            FlushDeadKeyState(dst);
        }

        return map;
    }

    private static TextScript ComputeLayoutScript(IntPtr hkl)
    {
        // Counting rather than sampling one key: some layouts park a stray Latin letter on an
        // otherwise Cyrillic board, and the majority is what actually identifies the layout.
        var counts = new Dictionary<TextScript, int>();
        var buffer = new StringBuilder(8);
        var unshifted = ShiftStates[0];

        try
        {
            for (var scan = FirstScanCode; scan <= LastScanCode; scan++)
            {
                var vk = NativeMethods.MapVirtualKeyEx(scan, NativeMethods.MAPVK_VSC_TO_VK_EX, hkl);
                if (vk == 0) continue;
                if (!TryTranslate(vk, scan, unshifted, hkl, buffer, out var c)) continue;

                var script = ScriptClassifier.Classify(c);
                if (script == TextScript.Neutral) continue;
                counts[script] = counts.GetValueOrDefault(script) + 1;
            }
        }
        finally
        {
            FlushDeadKeyState(hkl);
        }

        var best = TextScript.Neutral;
        var bestCount = 0;
        foreach (var pair in counts)
        {
            if (pair.Value <= bestCount) continue;
            best = pair.Key;
            bestCount = pair.Value;
        }
        return best;
    }

    /// <summary>
    /// Asks <paramref name="hkl"/> for the single character a key produces, rejecting anything that
    /// is not printable text.
    ///
    /// <para>A negative return from <c>ToUnicodeEx</c> means a DEAD key (the accent keys on French
    /// and Spanish layouts). Those do not merely fail — they park state in the thread's keyboard
    /// buffer that would corrupt the NEXT translation, so we immediately press the key again to
    /// consume it. Dead keys are then skipped: they produce a character only in combination with the
    /// key that follows, which is outside what a char-to-char map can express.</para>
    /// </summary>
    private static bool TryTranslate(uint vk, uint scan, byte[] keyState, IntPtr hkl, StringBuilder buffer, out char result)
    {
        result = '\0';

        buffer.Clear();
        var rc = NativeMethods.ToUnicodeEx(vk, scan, keyState, buffer, buffer.Capacity, 0, hkl);

        if (rc < 0)
        {
            // Dead key — replay it to clear the pending combining state, then decline.
            buffer.Clear();
            NativeMethods.ToUnicodeEx(vk, scan, keyState, buffer, buffer.Capacity, 0, hkl);
            return false;
        }

        // rc greater than 1 means a ligature/multi-char key, which has no place in a char map.
        if (rc != 1 || buffer.Length < 1) return false;

        var c = buffer[0];
        // Control characters: Enter, Tab, Escape and Backspace all live inside the scan range we walk.
        if (c < ' ' || c == '\u007F') return false;

        result = c;
        return true;
    }

    /// <summary>
    /// Consumes any pending dead-key state on the calling thread by pressing space twice — the
    /// standard trick, since a dead key followed by space resolves to the standalone accent and
    /// leaves the buffer clean. Cheap insurance: a leftover accent would silently corrupt the first
    /// character of the next map we build.
    /// </summary>
    private static void FlushDeadKeyState(IntPtr hkl)
    {
        try
        {
            var buffer = new StringBuilder(8);
            var state = ShiftStates[0];
            for (var i = 0; i < 2; i++)
            {
                buffer.Clear();
                NativeMethods.ToUnicodeEx(NativeMethods.VK_SPACE, NativeMethods.SCAN_SPACE,
                    state, buffer, buffer.Capacity, 0, hkl);
            }
        }
        catch
        {
            // Best-effort hygiene — never worth failing a conversion over.
        }
    }

    private static byte[][] BuildShiftStates()
    {
        var none = new byte[256];

        var shift = new byte[256];
        shift[NativeMethods.VK_SHIFT] = 0x80;

        // AltGr is delivered as Ctrl+Alt; layouts that use it read exactly this combination.
        var altGr = new byte[256];
        altGr[NativeMethods.VK_CONTROL] = 0x80;
        altGr[NativeMethods.VK_MENU] = 0x80;

        var shiftAltGr = new byte[256];
        shiftAltGr[NativeMethods.VK_SHIFT] = 0x80;
        shiftAltGr[NativeMethods.VK_CONTROL] = 0x80;
        shiftAltGr[NativeMethods.VK_MENU] = 0x80;

        return new[] { none, shift, altGr, shiftAltGr };
    }
}

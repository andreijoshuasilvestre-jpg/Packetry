using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "narrator is speaking" indicator along the bottom of an annotation
/// panel: a row of bars driven live by the voice's frequency spectrum while
/// its speaker's line plays (see NarrationManager.ReadSpectrum), easing back
/// to a flat line between lines. Hidden while narration is disabled, so a
/// panel with narration off looks exactly as it did before narration existed.
///
/// Mirrored around the middle with the lowest frequencies at the center —
/// speech lives mostly in the low bands, so it pulses outward from the middle
/// rather than piling up at one end. Heights are relative to the loudest band
/// heard recently (a slowly decaying peak), so the bars fill the strip at any
/// volume setting and still dip between words.
/// </summary>
public class NarrationWaveform : MonoBehaviour
{
    const int   BarCount     = 26;    // even: two mirrored halves
    const int   SpectrumSize = 256;   // GetSpectrumData bins across 0..Nyquist (~94 Hz each at 48 kHz)
    const int   MaxBin       = 48;    // ~4.5 kHz — speech has little energy above it
    const float BarFill      = 0.5f;  // bar width as a share of its slot
    const float IdleHeight   = 0.1f;  // the flat line, as a share of the strip's height
    const float Attack       = 25f;   // per-second easing toward a taller bar...
    const float Release      = 7f;    // ...and back down
    const float PeakDecay    = 0.6f;  // the reference peak keeps this much of itself per second
    const float NoiseFloor   = 1e-5f;

    readonly float[] spectrum = new float[SpectrumSize];
    object speaker;
    GameObject barsRoot;
    RectTransform[] bars;
    Image[] images;
    float[] levels;   // one per mirrored pair, 0..1, index 0 = the middle pair
    int[] bandEdges;  // spectrum bins of pair i: [bandEdges[i], bandEdges[i + 1])
    float peak;

    /// <summary>Adds the indicator under parent, reacting only to speaker's lines.
    /// The caller positions and sizes it through Rect.</summary>
    public static NarrationWaveform Create(Transform parent, object speaker, Color color)
    {
        var go = new GameObject("NarrationWaveform", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var wave = go.AddComponent<NarrationWaveform>();
        wave.Build(speaker, color);
        return wave;
    }

    public RectTransform Rect => (RectTransform)transform;

    public void SetColor(Color color)
    {
        foreach (var img in images) img.color = color;
    }

    void Build(object owner, Color color)
    {
        speaker = owner;

        barsRoot = new GameObject("Bars", typeof(RectTransform));
        barsRoot.transform.SetParent(transform, false);
        var rootRt = (RectTransform)barsRoot.transform;
        rootRt.anchorMin = Vector2.zero;
        rootRt.anchorMax = Vector2.one;
        rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;

        // Each bar spans its slot's middle BarFill horizontally; its height is
        // set through its vertical anchors (see SetPair), so it scales with
        // whatever size the strip is given.
        bars = new RectTransform[BarCount];
        images = new Image[BarCount];
        float slot = 1f / BarCount, inset = slot * (1f - BarFill) * 0.5f;
        for (int i = 0; i < BarCount; i++)
        {
            var go = new GameObject("Bar", typeof(RectTransform));
            go.transform.SetParent(barsRoot.transform, false);
            images[i] = go.AddComponent<Image>();
            images[i].raycastTarget = false;
            bars[i] = (RectTransform)go.transform;
            bars[i].anchorMin = new Vector2(i * slot + inset, 0.5f);
            bars[i].anchorMax = new Vector2((i + 1) * slot - inset, 0.5f);
            bars[i].offsetMin = bars[i].offsetMax = Vector2.zero;
        }
        SetColor(color);

        // Quadratic spacing: narrow bands down low where speech's energy is,
        // wider toward MaxBin, every band at least one bin wide.
        int pairs = BarCount / 2;
        levels = new float[pairs];
        bandEdges = new int[pairs + 1];
        for (int i = 0; i <= pairs; i++)
        {
            float t = (float)i / pairs;
            int edge = 1 + Mathf.RoundToInt((MaxBin - 1) * t * t);
            bandEdges[i] = i == 0 ? edge : Mathf.Max(edge, bandEdges[i - 1] + 1);
        }
        for (int i = 0; i < pairs; i++) SetPair(i, 0f);
    }

    void Update()
    {
        bool on = NarrationManager.Enabled;
        if (barsRoot.activeSelf != on) barsRoot.SetActive(on);
        if (!on) return;

        var voice = NarrationManager.Instance;
        bool speaking = voice != null && voice.ReadSpectrum(speaker, spectrum);
        float dt = Time.unscaledDeltaTime;

        float framePeak = speaking ? BinPeak(bandEdges[0], bandEdges[levels.Length]) : 0f;
        peak = Mathf.Max(framePeak, peak * Mathf.Pow(PeakDecay, dt));

        for (int i = 0; i < levels.Length; i++)
        {
            float target = speaking && peak > NoiseFloor
                ? Mathf.Sqrt(BinPeak(bandEdges[i], bandEdges[i + 1]) / peak)
                : 0f;
            float rate = target > levels[i] ? Attack : Release;
            levels[i] = Mathf.Lerp(levels[i], target, 1f - Mathf.Exp(-rate * dt));
            SetPair(i, levels[i]);
        }
    }

    float BinPeak(int from, int to)
    {
        float max = 0f;
        for (int b = from; b < to; b++) max = Mathf.Max(max, spectrum[b]);
        return max;
    }

    // Pair i is the two bars i steps out from the middle, mirrored.
    void SetPair(int i, float level)
    {
        float halfHeight = Mathf.Lerp(IdleHeight, 1f, level) * 0.5f;
        int pairs = levels.Length;
        SetHeight(bars[pairs - 1 - i], halfHeight);
        SetHeight(bars[pairs + i], halfHeight);
    }

    static void SetHeight(RectTransform bar, float halfHeight)
    {
        bar.anchorMin = new Vector2(bar.anchorMin.x, 0.5f - halfHeight);
        bar.anchorMax = new Vector2(bar.anchorMax.x, 0.5f + halfHeight);
    }
}

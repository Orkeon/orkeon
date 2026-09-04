// Shared financial math for the trading tool module (EX-01) — the TypeScript port
// of Orkeon.Trading.Tools' FinancialMathHelper plus the two primitives MathNet used
// to provide: a normal inverse CDF (Acklam's rational approximation) and a SEEDED
// PRNG (mulberry32). Every tool that used Random is deterministic now: same input,
// same output — which also retired the repository's CA5394 waiver.

export function mean(xs: readonly number[]): number {
    if (xs.length === 0) return 0;
    let s = 0;
    for (const x of xs) s += x;
    return s / xs.length;
}

/** Sample variance (n-1 denominator), matching MathNet's Variance(). */
export function variance(xs: readonly number[]): number {
    if (xs.length < 2) return 0;
    const m = mean(xs);
    let s = 0;
    for (const x of xs) s += (x - m) * (x - m);
    return s / (xs.length - 1);
}

export function stdDev(xs: readonly number[]): number {
    return Math.sqrt(variance(xs));
}

/** Pearson correlation of two equally long series; 0 when degenerate. */
export function pearson(a: readonly number[], b: readonly number[]): number {
    const n = Math.min(a.length, b.length);
    if (n < 2) return 0;
    const ma = mean(a.slice(-n));
    const mb = mean(b.slice(-n));
    let num = 0, da = 0, db = 0;
    for (let i = 0; i < n; i++) {
        const xa = a[a.length - n + i] - ma;
        const xb = b[b.length - n + i] - mb;
        num += xa * xb;
        da += xa * xa;
        db += xb * xb;
    }
    const den = Math.sqrt(da * db);
    return den === 0 ? 0 : num / den;
}

/** Linear-interpolated quantile of a series, q in [0, 1]. */
export function quantile(xs: readonly number[], q: number): number {
    if (xs.length === 0) return 0;
    const sorted = [...xs].sort((x, y) => x - y);
    const pos = Math.min(Math.max(q, 0), 1) * (sorted.length - 1);
    const lo = Math.floor(pos);
    const hi = Math.ceil(pos);
    return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
}

/** Simple returns from a price series: r[i] = p[i+1]/p[i] - 1. */
export function returnsFromPrices(prices: readonly number[]): number[] {
    const rets: number[] = [];
    for (let i = 1; i < prices.length; i++) {
        if (prices[i - 1] !== 0) rets.push(prices[i] / prices[i - 1] - 1);
    }
    return rets;
}

export type Matrix = Record<string, Record<string, number>>;

/**
 * Correlation matrix, aligned to the shortest common history — the exact
 * semantics of FinancialMathHelper.CalculateCorrelationMatrix (1 on the
 * diagonal, 0 for missing or degenerate pairs).
 */
export function correlationMatrix(
    symbols: readonly string[],
    returnsData: Record<string, readonly number[]>): Matrix {
    const out: Matrix = {};
    for (const s1 of symbols) {
        out[s1] = {};
        for (const s2 of symbols) {
            const r1 = returnsData[s1];
            const r2 = returnsData[s2];
            if (!r1 || !r2 || Math.min(r1.length, r2.length) < 2) {
                out[s1][s2] = s1 === s2 ? 1 : 0;
                continue;
            }
            out[s1][s2] = s1 === s2 ? 1 : pearson(r1, r2);
        }
    }
    return out;
}

/** Covariance matrix with the same alignment rules (sample variance diagonal). */
export function covarianceMatrix(
    symbols: readonly string[],
    returnsData: Record<string, readonly number[]>): Matrix {
    const out: Matrix = {};
    for (const s1 of symbols) {
        out[s1] = {};
        for (const s2 of symbols) {
            const r1 = returnsData[s1];
            const r2 = returnsData[s2];
            const n = r1 && r2 ? Math.min(r1.length, r2.length) : 0;
            if (n < 2) { out[s1][s2] = 0; continue; }
            const a1 = r1!.slice(-n);
            const a2 = r2!.slice(-n);
            out[s1][s2] = s1 === s2
                ? variance(a1)
                : pearson(a1, a2) * stdDev(a1) * stdDev(a2);
        }
    }
    return out;
}

/** sqrt(w' Σ w), floored at zero — FinancialMathHelper.CalculatePortfolioVolatility. */
export function portfolioVolatility(
    weights: Record<string, number>,
    covariance: Matrix): number {
    let v = 0;
    for (const [k1, w1] of Object.entries(weights)) {
        for (const [k2, w2] of Object.entries(weights)) {
            v += w1 * w2 * (covariance[k1]?.[k2] ?? 0);
        }
    }
    return Math.sqrt(Math.max(0, v));
}

/**
 * Inverse CDF of the standard normal (Acklam's rational approximation,
 * ~1e-9 absolute error) — the replacement for MathNet's Normal.InvCDF.
 */
export function normalInvCdf(p: number): number {
    if (p <= 0) return -Infinity;
    if (p >= 1) return Infinity;
    const a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02,
               1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
    const b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02,
               6.680131188771972e+01, -1.328068155288572e+01];
    const c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00,
               -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
    const d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00,
               3.754408661907416e+00];
    const pl = 0.02425;
    if (p < pl) {
        const q = Math.sqrt(-2 * Math.log(p));
        return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
            / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
    }
    if (p <= 1 - pl) {
        const q = p - 0.5;
        const r = q * q;
        return (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q
            / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
    }
    const q2 = Math.sqrt(-2 * Math.log(1 - p));
    return -(((((c[0] * q2 + c[1]) * q2 + c[2]) * q2 + c[3]) * q2 + c[4]) * q2 + c[5])
        / ((((d[0] * q2 + d[1]) * q2 + d[2]) * q2 + d[3]) * q2 + 1);
}

/** mulberry32 — a tiny, fast, SEEDED PRNG. Same seed, same stream, every run. */
export function mulberry32(seed: number): () => number {
    let a = seed >>> 0;
    return () => {
        a = (a + 0x6D2B79F5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

/** Deterministic seed derived from a string (FNV-1a), for per-input PRNG streams. */
export function seedFrom(text: string): number {
    let h = 0x811C9DC5;
    for (let i = 0; i < text.length; i++) {
        h ^= text.charCodeAt(i);
        h = Math.imul(h, 0x01000193);
    }
    return h >>> 0;
}

/** Standard-normal sampler over a seeded PRNG (Box–Muller). */
export function normalSampler(rand: () => number): () => number {
    return () => {
        const u1 = Math.max(rand(), 1e-12);
        const u2 = rand();
        return Math.sqrt(-2 * Math.log(u1)) * Math.cos(2 * Math.PI * u2);
    };
}

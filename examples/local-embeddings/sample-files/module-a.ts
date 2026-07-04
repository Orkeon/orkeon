// HTTP client utility for fetching JSON from REST APIs.
export async function fetchJson<T>(url: string): Promise<T> {
    const response = await fetch(url);
    return await response.json() as T;
}

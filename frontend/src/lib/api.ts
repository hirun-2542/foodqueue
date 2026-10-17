import type { ApiErrorCode, ProblemDetails } from "@/lib/types";

/**
 * A failed API call. `message` is Thai and safe to show as-is.
 * `code` is the server's stable error code, or null when the response carried none
 * (401/403, proxy errors, network failure: check `status` instead; 0 means the request never got an answer).
 */
export class ApiError extends Error {
  readonly code: ApiErrorCode | null;
  readonly status: number;

  constructor(code: ApiErrorCode | null, status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.code = code;
    this.status = status;
  }
}

function fallbackMessage(status: number): string {
  if (status === 0) return "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ตรวจสอบการเชื่อมต่อแล้วลองอีกครั้ง";
  if (status === 401) return "กรุณาเข้าสู่ระบบก่อนใช้งาน";
  if (status === 403) return "บัญชีนี้ไม่มีสิทธิ์ใช้งานส่วนนี้";
  if (status === 404) return "ไม่พบข้อมูลที่ต้องการ";
  if (status >= 500) return "เซิร์ฟเวอร์ขัดข้อง ลองอีกครั้งในอีกสักครู่";
  return "ทำรายการไม่สำเร็จ ลองอีกครั้ง";
}

async function toApiError(res: Response): Promise<ApiError> {
  let body: ProblemDetails | null = null;
  if (res.headers.get("content-type")?.includes("json")) {
    body = (await res.json().catch(() => null)) as ProblemDetails | null;
  }
  const code = (body?.code ?? null) as ApiErrorCode | null;
  // Only a coded FoodQueue problem carries Thai text in detail/title; framework defaults are English.
  const message =
    body?.message ?? (code ? (body?.detail ?? body?.title) : undefined) ?? fallbackMessage(res.status);
  return new ApiError(code, res.status, message);
}

/**
 * Same-origin JSON fetch against `/api`. Sends the auth cookie, sets JSON headers for a JSON body,
 * returns the parsed body (undefined for 204), and throws `ApiError` for any non-2xx answer.
 */
export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  headers.set("Accept", "application/json");
  if (typeof init.body === "string" && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  let res: Response;
  try {
    res = await fetch(path, { ...init, headers, credentials: "include" });
  } catch (err) {
    if (err instanceof DOMException && err.name === "AbortError") throw err;
    throw new ApiError(null, 0, fallbackMessage(0));
  }

  if (!res.ok) throw await toApiError(res);
  if (res.status === 204 || res.headers.get("content-length") === "0") return undefined as T;
  return (await res.json()) as T;
}

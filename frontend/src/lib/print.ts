"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { apiFetchWithServerTime } from "@/lib/api";
import { useLiveEvent } from "@/lib/realtime";
import type { PaidBillDto, PaymentMethod, PrintDataDto, PrintDocument } from "@/lib/types";

// งานพิมพ์ของ ticket 20: ข้อมูลพิมพ์ 80mm และประวัติบิลที่ชำระแล้ว

/** วิธีจ่ายบนกระดาษและในตารางประวัติเป็นไทย — enum ของ API เป็นอังกฤษ (frame p1/o15) */
export const paymentMethodLabel: Record<PaymentMethod, string> = {
  Cash: "เงินสด",
  PromptPay: "PromptPay",
  Card: "บัตร",
};

export const printDataQueryKey = (sessionId: number, doc: PrintDocument) =>
  ["print", sessionId, doc] as const;

/**
 * ข้อมูลของเอกสารพิมพ์หนึ่งใบ (`GET /api/sessions/{id}/print-data?doc=`).
 *
 * `doc` ถูกส่งไปกับคำขอจริงเพราะเงื่อนไขของเอกสารอยู่ที่เซิร์ฟเวอร์: ใบเสร็จของรอบที่ยังไม่ Paid
 * ถูกปฏิเสธด้วย `SESSION_CLOSED` และ QR slip ของรอบที่ปิดแล้วเช่นกัน หน้าจอจึงไม่ต้องเดาเงื่อนไขเอง
 * และเวลาที่พิมพ์ใช้ `serverNow` (จาก header `Date`) ไม่ใช่นาฬิกาของเครื่องที่ต่ออยู่กับเครื่องพิมพ์
 */
export function usePrintData(sessionId: number, doc: PrintDocument) {
  return useQuery({
    queryKey: printDataQueryKey(sessionId, doc),
    queryFn: async (): Promise<{ data: PrintDataDto; serverNow: number }> => {
      const answer = await apiFetchWithServerTime<PrintDataDto>(
        `/api/sessions/${sessionId}/print-data?doc=${doc}`,
      );
      return { data: answer.data, serverNow: answer.serverNow };
    },
    enabled: Number.isInteger(sessionId) && sessionId > 0,
    // เอกสารพิมพ์เป็นภาพนิ่งของบิล ณ ตอนเปิดหน้า: ไม่ refetch ตาม hub ระหว่างพิมพ์
    staleTime: Infinity,
    retry: false,
  });
}

export const historyQueryKey = (day: string | null) => ["sessions", "history", day ?? "today"] as const;

/**
 * บิลที่ชำระแล้วของวันหนึ่งตามเวลาไทย (`GET /api/sessions/history?date=`), เรียงล่าสุดก่อน.
 *
 * `day = null` = ยังไม่รู้วันของเซิร์ฟเวอร์ จึงไม่ส่ง `date` ไป: เซิร์ฟเวอร์ตอบ "วันนี้" ตามเวลาไทยของมันเอง
 * แล้วหน้าจอตั้งวันที่จาก `serverNow` ของคำตอบนั้น — วันของร้านจึงไม่ขึ้นกับนาฬิกาของเครื่องที่เปิดหน้า
 *
 * อัปเดตเองเมื่อมีการปิดบิล: `sessionClosed` มาจากการชำระเงินของรอบไหนก็ได้ในร้าน (hub ไม่ได้บอกว่าใบไหน)
 * จึง invalidate ทั้งคีย์ของวันนั้น และหลัง reconnect ทุก query ถูก invalidate อยู่แล้ว
 */
export function usePaidHistory(day: string | null) {
  const queryClient = useQueryClient();
  const queryKey = historyQueryKey(day);
  const query = useQuery({
    queryKey,
    queryFn: async (): Promise<{ bills: PaidBillDto[]; serverNow: number }> => {
      const answer = await apiFetchWithServerTime<PaidBillDto[]>(
        day == null ? "/api/sessions/history" : `/api/sessions/history?date=${encodeURIComponent(day)}`,
      );
      return { bills: answer.data, serverNow: answer.serverNow };
    },
  });

  useLiveEvent("sessionClosed", () => void queryClient.invalidateQueries({ queryKey: historyQueryKey(day) }));

  return query;
}

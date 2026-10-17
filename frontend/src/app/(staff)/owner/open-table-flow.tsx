"use client";

import { QRCodeSVG } from "qrcode.react";
import { useQuery } from "@tanstack/react-query";
import { CheckIcon, LinkIcon, MinusIcon, PlusIcon, PrinterIcon, QrCodeIcon } from "lucide-react";
import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError, apiFetch } from "@/lib/api";
import { formatBaht, formatBahtWhole, formatTime } from "@/lib/format";
import { useOpenSession, useOpenSessions, type OpenSessionInput } from "@/lib/sessions";
import { useTables } from "@/lib/tables";
import type { PackageDto, SessionDetailDto } from "@/lib/types";

/**
 * The open-table flow of frames `o02-open` and `o03-qr`: pick a free table, choose a package and the
 * guest counts, then show the QR of the round that was just created.
 *
 * The whole table grid of `o01-overview` (KPI cards, ใช้ไป N นาที, ยอดสะสม) belongs to ticket 08 and
 * the report section to ticket 23; what is here is the picker plus both dialogs, cut to what this
 * ticket can prove against the real API.
 */
export function OpenTableFlow() {
  const tables = useTables();
  const openSessions = useOpenSessions();
  const packages = useQuery({
    queryKey: ["packages"],
    queryFn: () => apiFetch<PackageDto[]>("/api/packages"),
  });
  const openSession = useOpenSession();

  const [openTableId, setOpenTableId] = useState<number | null>(null);
  // The "opens at / ends at" preview is a snapshot taken when the owner opens the dialog.
  // Reading the clock during render would make the same render produce different text over time.
  const [previewStart, setPreviewStart] = useState<number | null>(null);
  const [created, setCreated] = useState<SessionDetailDto | null>(null);

  const takenByTable = openSessions.data ?? [];
  const activePackages = (packages.data ?? []).filter((p) => p.isActive);
  const selectedTable = (tables.data ?? []).find((t) => t.id === openTableId) ?? null;

  const openFor = (tableId: number) => {
    openSession.reset();
    setPreviewStart(Date.now());
    setOpenTableId(tableId);
  };

  const submit = async (input: OpenSessionInput) => {
    try {
      setCreated(await openSession.mutateAsync(input));
      setOpenTableId(null);
      setPreviewStart(null);
    } catch {
      // The dialog shows `openSession.error` next to the button that caused it.
    }
  };

  if (tables.isPending || openSessions.isPending) {
    return <Skeleton className="h-40 w-full rounded-xl" />;
  }

  if (tables.isError) {
    return (
      <p className="text-body text-muted-foreground">
        {tables.error instanceof ApiError
          ? tables.error.message
          : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"}
      </p>
    );
  }

  const floor = (tables.data ?? []).filter((t) => t.isActive);

  return (
    <section className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-4">
        <h2 className="text-title">โต๊ะ</h2>
        <span className="text-label font-normal text-muted-foreground">
          {floor.length} โต๊ะ · ว่าง{" "}
          {floor.filter((t) => !takenByTable.some((s) => s.tableId === t.id)).length}
        </span>
      </div>

      <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        {floor.map((table) => {
          const session = takenByTable.find((s) => s.tableId === table.id);
          return (
            <li
              key={table.id}
              className={`flex flex-col gap-2 rounded-xl border bg-surface p-4 ${
                session ? "border-border" : "border-dashed border-line-strong"
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <b className="text-title">{table.name}</b>
                <span className="text-label font-semibold text-muted-foreground">
                  {session ? "กำลังใช้" : "ว่าง"}
                </span>
              </div>

              {session ? (
                <>
                  <span className="text-label text-muted-foreground">
                    {session.packageName} · {session.adults} ผู้ใหญ่
                    {session.children > 0 ? ` ${session.children} เด็ก` : ""}
                  </span>
                  <span className="font-mono text-label">
                    {formatTime(session.openedAt)}–{formatTime(session.endsAt)}
                  </span>
                </>
              ) : (
                <>
                  <span className="text-label text-muted-foreground">{table.seats} ที่นั่ง</span>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    className="mt-1 self-start"
                    onClick={() => openFor(table.id)}
                  >
                    <PlusIcon aria-hidden />
                    เปิดโต๊ะ
                  </Button>
                </>
              )}
            </li>
          );
        })}
      </ul>

      {selectedTable ? (
        <OpenTableDialog
          tableId={selectedTable.id}
          tableName={selectedTable.name}
          packages={activePackages}
          isPackagesLoading={packages.isPending}
          error={openSession.error}
          saving={openSession.isPending}
          previewStart={previewStart ?? undefined}
          onCancel={() => setOpenTableId(null)}
          onSubmit={submit}
        />
      ) : null}

      {created ? <QrResultDialog session={created} onClose={() => setCreated(null)} /> : null}
    </section>
  );
}

/**
 * Frame `o02-open`. The guest counts start at 2 adults / 1 child like the frame, and the buffet total
 * in the well is the same arithmetic the server snapshots when the round is created.
 */
function OpenTableDialog({
  tableId,
  tableName,
  packages,
  isPackagesLoading,
  error,
  saving,
  previewStart,
  onCancel,
  onSubmit,
}: {
  tableId: number;
  tableName: string;
  packages: PackageDto[];
  isPackagesLoading: boolean;
  error: unknown;
  saving: boolean;
  /** Snapshot of the clock taken when the owner opened this dialog; pure to render. */
  previewStart?: number;
  onCancel: () => void;
  onSubmit: (input: OpenSessionInput) => Promise<void>;
}) {
  const [packageId, setPackageId] = useState<number | null>(packages[0]?.id ?? null);
  const [adults, setAdults] = useState(2);
  const [children, setChildren] = useState(1);

  const chosen = packages.find((p) => p.id === packageId) ?? null;
  const buffetTotal = chosen ? adults * chosen.adultPrice + children * chosen.childPrice : 0;

  return (
    <Dialog open onOpenChange={(next) => (next ? undefined : onCancel())}>
      <DialogContent className="sm:max-w-[560px]">
        <DialogHeader>
          <DialogTitle className="text-headline">เปิดโต๊ะ {tableName}</DialogTitle>
          <DialogDescription>
            เลือกแพ็กเกจและจำนวนผู้ใหญ่/เด็ก ระบบจะสร้าง QR สำหรับรอบการนั่งนี้
          </DialogDescription>
        </DialogHeader>

        <div className="flex flex-col gap-2">
          <div className="flex items-center justify-between gap-3">
            <span className="text-label text-ink-2">แพ็กเกจ</span>
            <span className="text-xs text-muted-foreground">แพ็กเกจอันดับสูงสั่งเมนูได้มากกว่า</span>
          </div>

          {isPackagesLoading ? (
            <Skeleton className="h-16 w-full rounded-lg" />
          ) : packages.length === 0 ? (
            <p className="rounded-lg border border-border bg-sunken p-3 text-body text-muted-foreground">
              ยังไม่มีแพ็กเกจที่ใช้งานอยู่ — เปิดใช้งานแพ็กเกจในหน้า “แพ็กเกจ” ก่อนเปิดโต๊ะ
            </p>
          ) : (
            <ul className="flex flex-col gap-2">
              {packages.map((pkg) => {
                const on = pkg.id === packageId;
                return (
                  <li key={pkg.id}>
                    <button
                      type="button"
                      aria-pressed={on}
                      onClick={() => setPackageId(pkg.id)}
                      className={`flex w-full items-center gap-3 rounded-lg border px-3.5 py-3 text-left ${
                        on
                          ? "border-orange-line bg-orange-soft"
                          : "border-border bg-surface hover:bg-sunken"
                      }`}
                    >
                      <span className="flex flex-1 flex-col gap-1">
                        <span className="flex items-center gap-2">
                          <b className={`text-title ${on ? "text-orange-ink" : ""}`}>{pkg.name}</b>
                          <span className="text-xs text-muted-foreground">อันดับ {pkg.rank}</span>
                        </span>
                        <span className="text-label text-muted-foreground">
                          ผู้ใหญ่ {formatBahtWhole(pkg.adultPrice)} · เด็ก {formatBahtWhole(pkg.childPrice)} ·{" "}
                          {pkg.durationMinutes} นาที
                        </span>
                      </span>
                      <span
                        className={`flex size-6 flex-none items-center justify-center rounded-md border ${
                          on ? "border-orange-line bg-primary text-primary-foreground" : "border-control-border"
                        }`}
                      >
                        {on ? <CheckIcon className="size-4" aria-hidden /> : null}
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>

        <div className="flex flex-col gap-3.5">
          <CountStepper
            id="adults"
            label="ผู้ใหญ่"
            hint="ต้องมีอย่างน้อย 1 ท่าน"
            value={adults}
            min={1}
            onChange={setAdults}
          />
          <CountStepper
            id="children"
            label="เด็ก"
            hint="ไม่บังคับ"
            value={children}
            min={0}
            onChange={setChildren}
          />
        </div>

        <div className="rounded-lg bg-sunken p-4">
          <div className="flex items-center justify-between gap-4">
            <span className="text-label">
              ค่าบุฟเฟ่ต์ {adults} × {formatBahtWhole(chosen?.adultPrice ?? 0)} + {children} ×{" "}
              {formatBahtWhole(chosen?.childPrice ?? 0)}
            </span>
            <b className="text-title tabular-nums">{formatBaht(buffetTotal)}</b>
          </div>
          <p className="mt-1.5 text-label text-muted-foreground">
            {previewStart === undefined ? (
              <>ยังไม่รวมค่าบริการและ VAT</>
            ) : (
              <>
                เปิด <span className="font-mono">{formatTime(previewStart)}</span> · หมดเวลา{" "}
                <span className="font-mono">
                  {formatTime(previewStart + (chosen?.durationMinutes ?? 0) * 60_000)}
                </span>{" "}
                · ยังไม่รวมค่าบริการและ VAT
              </>
            )}
          </p>
        </div>

        {error ? (
          <p role="alert" className="text-body text-cancel-ink">
            {error instanceof ApiError ? error.message : "เปิดโต๊ะไม่สำเร็จ ลองอีกครั้ง"}
          </p>
        ) : null}

        <DialogFooter className="border-t-0 bg-transparent">
          <Button type="button" variant="ghost" onClick={onCancel}>
            ยกเลิก
          </Button>
          <Button
            type="button"
            size="lg"
            disabled={saving || chosen === null}
            onClick={() =>
              void onSubmit({ tableId, packageId: chosen!.id, adultCount: adults, childCount: children })
            }
          >
            <QrCodeIcon aria-hidden />
            เปิดโต๊ะและสร้าง QR
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Frame `o03-qr`. The QR is the real `qrUrl` from the server (`{App:PublicBaseUrl}/s/{token}`), so a
 * phone on the same network opens the round it just created. The QR-slip print page is ticket 20's
 * route, linked here and not faked.
 */
function QrResultDialog({ session, onClose }: { session: SessionDetailDto; onClose: () => void }) {
  const durationMinutes = Math.round(
    (Date.parse(session.endsAt) - Date.parse(session.openedAt)) / 60_000,
  );

  return (
    <Dialog open onOpenChange={(next) => (next ? undefined : onClose())}>
      <DialogContent className="sm:max-w-[560px]">
        <DialogHeader>
          <DialogTitle className="text-headline">โต๊ะ {session.tableName} เปิดแล้ว</DialogTitle>
          <DialogDescription>พิมพ์ slip ไปวางที่โต๊ะ หรือให้ลูกค้าสแกนจากจอนี้ได้เลย</DialogDescription>
        </DialogHeader>

        <p className="flex items-center gap-2 rounded-lg bg-ready-soft px-3 py-2.5 text-label text-ready-ink">
          <CheckIcon className="size-4 flex-none" aria-hidden />
          สร้าง QR สำหรับรอบการนั่งนี้แล้ว · ใช้ได้จนกว่าจะปิดบิล
        </p>

        <div className="mx-auto flex size-[244px] items-center justify-center rounded-xl border border-border bg-surface">
          <QRCodeSVG value={session.qrUrl} size={222} level="M" />
        </div>

        <div className="flex items-center justify-center gap-2">
          <span className="max-w-[240px] truncate font-mono text-label text-muted-foreground">
            {session.qrUrl.replace(/^https?:\/\/[^/]+/, "…")}
          </span>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() =>
              void navigator.clipboard
                .writeText(session.qrUrl)
                .then(() => toast.success("คัดลอกลิงก์แล้ว"))
                .catch(() => toast.error("คัดลอกไม่สำเร็จ — คัดลอกจากช่องที่อยู่แทน"))
            }
          >
            <LinkIcon aria-hidden />
            คัดลอกลิงก์
          </Button>
        </div>

        <dl className="flex flex-col gap-1 rounded-lg bg-sunken p-4 text-label">
          <div className="flex items-center justify-between gap-3">
            <dt className="text-muted-foreground">แพ็กเกจ</dt>
            <dd className="font-semibold">
              {session.packageName} · {durationMinutes} นาที
            </dd>
          </div>
          <div className="flex items-center justify-between gap-3">
            <dt className="text-muted-foreground">จำนวน</dt>
            <dd className="font-semibold">
              {session.adults} ผู้ใหญ่{session.children > 0 ? ` ${session.children} เด็ก` : ""}
            </dd>
          </div>
          <div className="flex items-center justify-between gap-3">
            <dt className="text-muted-foreground">เวลา</dt>
            <dd className="font-semibold">
              เปิด <span className="font-mono">{formatTime(session.openedAt)}</span> · หมด{" "}
              <span className="font-mono">{formatTime(session.endsAt)}</span>
            </dd>
          </div>
        </dl>

        <DialogFooter className="border-t-0 bg-transparent">
          <Button type="button" variant="ghost" onClick={onClose}>
            เสร็จสิ้น
          </Button>
          {/* หน้าพิมพ์ 80mm เป็นของ ticket 20 — ลิงก์ชี้ไปที่เส้นทางจริงของมันและปิด prefetch
              เพราะเส้นทางนั้นยังไม่มี จนกว่า ticket 20 จะสร้าง (ไม่พิมพ์เองที่นี่) */}
          <Button asChild size="lg">
            <Link href={`/print/qr/${session.id}`} prefetch={false} target="_blank" rel="noreferrer">
              <PrinterIcon aria-hidden />
              พิมพ์ QR slip
            </Link>
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/** The frame's round −/+ control. The count is a button-free readout between the two buttons. */
function CountStepper({
  id,
  label,
  hint,
  value,
  min,
  onChange,
}: {
  id: string;
  label: string;
  hint: string;
  value: number;
  min: number;
  onChange: (next: number) => void;
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <span className="flex flex-col">
        <span id={`${id}-label`} className="text-label text-ink-2">
          {label}
        </span>
        <span className="text-xs text-muted-foreground">{hint}</span>
      </span>
      <span className="flex items-center gap-3">
        <Button
          type="button"
          variant="outline"
          size="icon"
          aria-label={`ลด${label}`}
          disabled={value <= min}
          onClick={() => onChange(Math.max(min, value - 1))}
        >
          <MinusIcon aria-hidden />
        </Button>
        <b aria-labelledby={`${id}-label`} className="w-6 text-center text-title tabular-nums">
          {value}
        </b>
        <Button
          type="button"
          size="icon"
          className="rounded-full"
          aria-label={`เพิ่ม${label}`}
          onClick={() => onChange(value + 1)}
        >
          <PlusIcon aria-hidden />
        </Button>
      </span>
    </div>
  );
}

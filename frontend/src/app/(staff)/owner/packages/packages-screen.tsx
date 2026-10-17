"use client";

import { PencilIcon, PlusIcon } from "lucide-react";
import { useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Switch } from "@/components/ui/switch";
import { ApiError } from "@/lib/api";
import { formatBahtWhole } from "@/lib/format";
import type { PackageDto } from "@/lib/types";
import { draftOf, usePackages, type PackageDraft, type PackageFormErrors } from "./use-packages";

/**
 * `/owner/packages` — the screen from frame `o11-packages`: the tier table on top, the edit form
 * bottom left, the per-tier buffet item ladder bottom right.
 *
 * The ladder counts real buffet items once the menu API exists (ticket 05); until then it says the
 * menu is unavailable rather than showing the frame's numbers, which came from its seeded menu.
 */
export function PackagesScreen() {
  const { packages, isLoading, isError, error, save, buffetItems, isMenuLoading, isMenuUnavailable } =
    usePackages();
  const [editingId, setEditingId] = useState<number | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [draft, setDraft] = useState<PackageDraft | null>(null);
  const [errors, setErrors] = useState<PackageFormErrors>({});
  const [saving, setSaving] = useState(false);

  const rows = packages ?? [];
  const editing = rows.find((p) => p.id === editingId) ?? null;

  const openCreate = () => {
    setIsCreating(true);
    setEditingId(null);
    setErrors({});
    setDraft({
      name: "",
      rank: String(rows.reduce((max, p) => Math.max(max, p.rank), 0) + 1),
      adultPrice: "",
      childPrice: "",
      durationMinutes: "90",
      isActive: true,
    });
  };

  const openEdit = (pkg: PackageDto) => {
    setIsCreating(false);
    setEditingId(pkg.id);
    setErrors({});
    setDraft(draftOf(pkg));
  };

  const closeForm = () => {
    setIsCreating(false);
    setEditingId(null);
    setDraft(null);
    setErrors({});
  };

  const submit = async () => {
    if (!draft) return;
    setSaving(true);
    try {
      // An unsaved draft keeps the form open and shows the errors; a saved one stays open on the
      // row it just wrote, which is also how the frame shows the form.
      const result = await save(isCreating ? null : editingId, draft);
      if (result.ok) {
        setIsCreating(false);
        setEditingId(result.id);
        setErrors({});
      } else {
        setErrors(result.errors);
      }
    } finally {
      setSaving(false);
    }
  };

  /** The table's switch is the same edit as the form's, so it saves immediately. */
  const toggleActive = async (pkg: PackageDto, isActive: boolean) => {
    setSaving(true);
    try {
      const result = await save(pkg.id, { ...draftOf(pkg), isActive });
      if (!result.ok) setErrors(result.errors);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="flex flex-col">
      <div className="flex min-h-20 items-center justify-between gap-4">
        <div className="flex flex-col">
          <h1 className="text-headline">แพ็กเกจ</h1>
          <span className="text-label font-normal text-muted-foreground">
            {rows.length} ระดับ · ระดับสูงสั่งเมนูของระดับต่ำกว่าได้ด้วย
          </span>
        </div>
        <div className="flex items-center gap-3">
          <Button type="button" onClick={openCreate}>
            <PlusIcon aria-hidden />
            เพิ่มแพ็กเกจ
          </Button>
          <span className="inline-flex h-[22px] items-center rounded-xs bg-sunken px-2 text-[11.5px] font-semibold tracking-[0.02em] text-ink-2">
            DEMO DATA
          </span>
        </div>
      </div>

      {isLoading ? (
        <Card className="gap-3">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-14 w-full" />
          <Skeleton className="h-14 w-full" />
        </Card>
      ) : isError ? (
        <Card className="gap-2">
          <b className="text-title">โหลดแพ็กเกจไม่ได้</b>
          <span className="text-body text-muted-foreground">
            {error instanceof ApiError ? error.message : "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองอีกครั้งเมื่อเครือข่ายกลับมา"}
          </span>
        </Card>
      ) : rows.length === 0 ? (
        <Card className="gap-2">
          <b className="text-title">ยังไม่มีแพ็กเกจ</b>
          <span className="text-body text-muted-foreground">
            กด “เพิ่มแพ็กเกจ” เพื่อสร้างระดับแรก เช่น Silver ฿299 ผู้ใหญ่ ฿149 เด็ก 90 นาที
          </span>
        </Card>
      ) : (
        <div className="overflow-hidden rounded-xl border border-border bg-surface">
          <table className="w-full border-collapse text-[14px]">
            <thead>
              <tr className="bg-sunken">
                <Th className="w-[110px]">ระดับ</Th>
                <Th>ชื่อแพ็กเกจ</Th>
                <Th className="w-[190px] justify-end">ราคาผู้ใหญ่</Th>
                <Th className="w-[190px] justify-end">ราคาเด็ก</Th>
                <Th className="w-[170px]">เวลา</Th>
                <Th className="w-[150px]">ใช้งาน</Th>
                <Th className="w-[70px]">{""}</Th>
              </tr>
            </thead>
            <tbody>
              {rows.map((pkg) => (
                <tr
                  key={pkg.id}
                  className={`border-t border-border ${editingId === pkg.id ? "bg-orange-soft" : ""}`}
                >
                  <td className="h-14 px-4">
                    <span className="inline-flex size-[26px] items-center justify-center rounded-full bg-sunken text-[13px] font-semibold text-ink-2 tabular-nums">
                      {pkg.rank}
                    </span>
                  </td>
                  <td className="h-14 px-4 font-semibold">{pkg.name}</td>
                  <td className="h-14 px-4 text-right font-semibold tabular-nums">
                    {formatBahtWhole(pkg.adultPrice)}
                  </td>
                  <td className="h-14 px-4 text-right tabular-nums">{formatBahtWhole(pkg.childPrice)}</td>
                  <td className="h-14 px-4 text-ink-2 tabular-nums">{pkg.durationMinutes} นาที</td>
                  <td className="h-14 px-4">
                    <Switch
                      checked={pkg.isActive}
                      aria-label={`ใช้งาน ${pkg.name}`}
                      disabled={saving}
                      onCheckedChange={(checked) => void toggleActive(pkg, checked)}
                    />
                  </td>
                  <td className="h-14 px-4 text-right">
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      aria-label={`แก้ไขแพ็กเกจ ${pkg.name}`}
                      onClick={() => openEdit(pkg)}
                    >
                      <PencilIcon aria-hidden />
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div className="mt-4 grid items-start gap-4 xl:grid-cols-[1fr_400px]">
        <Card className="gap-0">
          {draft ? (
            <PackageForm
              draft={draft}
              errors={errors}
              title={isCreating ? "เพิ่มแพ็กเกจ" : `แก้ไขแพ็กเกจ · ${editing?.name ?? ""}`}
              savedRank={isCreating ? null : (editing?.rank ?? null)}
              saving={saving}
              onChange={setDraft}
              onCancel={closeForm}
              onSave={submit}
            />
          ) : (
            <div className="flex flex-col gap-2 py-6">
              <b className="text-base font-semibold">แก้ไขแพ็กเกจ</b>
              <span className="text-body text-muted-foreground">
                เลือกแพ็กเกจจากตารางด้านบน หรือกด “เพิ่มแพ็กเกจ” เพื่อสร้างระดับใหม่
              </span>
            </div>
          )}
        </Card>

        <ItemLadder
          rows={rows}
          buffetItems={buffetItems ?? []}
          isLoading={isMenuLoading}
          isUnavailable={isMenuUnavailable}
        />
      </div>
    </div>
  );
}

function Th({ className = "", children }: { className?: string; children: ReactNode }) {
  return (
    <th className={`px-4 py-2.5 text-left text-[12.5px] font-semibold text-muted-foreground whitespace-nowrap ${className}`}>
      {children}
    </th>
  );
}

/** Frame o11's right-hand card: what each tier is allowed, counted from the real buffet menu. */
function ItemLadder({
  rows,
  buffetItems,
  isLoading,
  isUnavailable,
}: {
  rows: readonly PackageDto[];
  buffetItems: readonly { id: number; name: string; minPackageRank: number | null }[];
  isLoading: boolean;
  isUnavailable: boolean;
}) {
  const ordered = [...rows].sort((a, b) => a.rank - b.rank);

  if (isLoading) {
    return (
      <Card className="gap-3">
        <Skeleton className="h-6 w-40" />
        <Skeleton className="h-14 w-full" />
        <Skeleton className="h-14 w-full" />
      </Card>
    );
  }

  return (
    <Card className="gap-0">
      <div className="flex flex-col gap-1">
        <h2 className="text-base font-semibold">สิทธิ์เมนูตามระดับ</h2>
        <span className="text-label font-normal text-muted-foreground">
          {isUnavailable
            ? "ยังนับไม่ได้ · รอหน้าจัดการเมนูอาหาร (ticket 05) ในระบบ"
            : `นับเฉพาะเมนูบุฟเฟ่ต์ ${buffetItems.length} รายการ จากทั้งหมด ${buffetItems.length} รายการ`}
        </span>
      </div>

      {ordered.length === 0 ? (
        <p className="mt-4 text-body text-muted-foreground">
          สร้างแพ็กเกจก่อน จึงจะเห็นสิทธิ์เมนูแต่ละระดับ
        </p>
      ) : (
        <ol className="mt-4 flex flex-col">
          {ordered.map((pkg, index) => {
            const allowed = buffetItems.filter(
              (item) => item.minPackageRank === null || item.minPackageRank <= pkg.rank,
            );
            const previous = index === 0 ? null : ordered[index - 1];
            const added =
              previous === null
                ? allowed
                : allowed.filter((item) => (item.minPackageRank ?? 0) > previous.rank);
            const listed =
              added
                .slice(0, 6)
                .map((item) => item.name)
                .join(", ") + (added.length > 6 ? "…" : "");
            const note =
              previous === null
                ? `เมนูระดับ ${pkg.rank} ทั้งหมด · ${listed}`
                : added.length === 0
                  ? "ไม่มีเมนูเพิ่มจากระดับก่อน"
                  : `+${added.length} จากระดับก่อน · ${listed}`;
            return (
              <li key={pkg.id} className="relative flex gap-3 pb-4 last:pb-0">
                {index < ordered.length - 1 ? (
                  <span className="absolute top-[34px] bottom-1.5 left-[13px] w-px bg-border" aria-hidden />
                ) : null}
                <span className="inline-flex size-[26px] flex-none items-center justify-center rounded-full bg-sunken text-[13px] font-semibold text-ink-2 tabular-nums">
                  {pkg.rank}
                </span>
                <div className="flex grow flex-col gap-1">
                  <div className="flex items-center justify-between gap-2">
                    <b>{pkg.name}</b>
                    <span className="text-label font-semibold tabular-nums">
                      {isUnavailable ? "—" : `${allowed.length} เมนู`}
                    </span>
                  </div>
                  <span className="text-[12px] leading-[1.45] text-muted-foreground">
                    {isUnavailable ? "นับจำนวนเมนูได้เมื่อหน้าจัดการเมนูอาหารพร้อมใช้งาน" : note}
                  </span>
                </div>
              </li>
            );
          })}
        </ol>
      )}

      <hr className="my-4 border-border" />
      <div className="flex flex-col gap-1.5">
        <span className="text-label font-medium">เมนูคิดเงินแยก สั่งได้ทุกระดับ</span>
        <span className="text-[12px] leading-[1.45] text-muted-foreground">
          แพ็กเกจที่ระดับสูงกว่าสั่งเมนูของระดับต่ำกว่าได้ด้วย
        </span>
      </div>
    </Card>
  );
}

function PackageForm({
  draft,
  errors,
  title,
  savedRank,
  saving,
  onChange,
  onCancel,
  onSave,
}: {
  draft: PackageDraft;
  errors: PackageFormErrors;
  title: string;
  /** The stored rank, or null while creating. The badge shows this, not the box being edited. */
  savedRank: number | null;
  saving: boolean;
  onChange: (draft: PackageDraft) => void;
  onCancel: () => void;
  onSave: () => void;
}) {
  const set = <K extends keyof PackageDraft>(key: K, value: PackageDraft[K]) =>
    onChange({ ...draft, [key]: value });

  return (
    <form
      className="flex flex-col"
      onSubmit={(event) => {
        event.preventDefault();
        onSave();
      }}
    >
      <div className="flex items-center justify-between gap-3">
        <h2 className="text-base font-semibold">{title}</h2>
        {savedRank === null ? null : (
          <span className="inline-flex h-6 items-center rounded-full bg-orange-soft px-2.5 text-[12.5px] font-semibold text-orange-ink">
            ระดับ {savedRank}
          </span>
        )}
      </div>

      <div className="mt-4 grid gap-x-4 gap-y-3.5 sm:grid-cols-2">
        <FormField id="package-name" label="ชื่อแพ็กเกจ" error={errors.name}>
          <Input
            id="package-name"
            value={draft.name}
            autoComplete="off"
            aria-invalid={Boolean(errors.name)}
            onChange={(event) => set("name", event.target.value)}
          />
        </FormField>

        <FormField id="package-rank" label="ระดับ" error={errors.rank}>
          <Input
            id="package-rank"
            className="tabular-nums"
            inputMode="numeric"
            value={draft.rank}
            aria-invalid={Boolean(errors.rank)}
            onChange={(event) => set("rank", event.target.value)}
          />
        </FormField>

        <FormField id="package-adult" label="ราคาผู้ใหญ่" error={errors.adultPrice}>
          <BahtInput
            id="package-adult"
            value={draft.adultPrice}
            invalid={Boolean(errors.adultPrice)}
            onChange={(value) => set("adultPrice", value)}
          />
        </FormField>

        <FormField id="package-child" label="ราคาเด็ก" error={errors.childPrice}>
          <BahtInput
            id="package-child"
            value={draft.childPrice}
            invalid={Boolean(errors.childPrice)}
            onChange={(value) => set("childPrice", value)}
          />
        </FormField>

        <FormField id="package-duration" label="เวลา (นาที)" error={errors.durationMinutes}>
          <Input
            id="package-duration"
            className="tabular-nums"
            inputMode="numeric"
            value={draft.durationMinutes}
            aria-invalid={Boolean(errors.durationMinutes)}
            onChange={(event) => set("durationMinutes", event.target.value)}
          />
        </FormField>

        <div className="flex flex-col gap-1.5">
          <Label htmlFor="package-active">ใช้งาน</Label>
          <div className="flex h-11 items-center gap-2.5">
            <Switch
              id="package-active"
              checked={draft.isActive}
              onCheckedChange={(checked) => set("isActive", checked)}
            />
            <span className="text-label font-normal text-muted-foreground">แสดงในหน้าเปิดโต๊ะ</span>
          </div>
        </div>
      </div>

      <div className="mt-4 flex items-end justify-between gap-3 border-t border-border pt-4">
        <div className="flex flex-col gap-1.5">
          <Button
            type="button"
            variant="destructive"
            disabled={saving || !draft.isActive}
            onClick={() => set("isActive", false)}
          >
            ปิดใช้งาน
          </Button>
          <span className="text-[12px] leading-[1.45] text-muted-foreground">
            ปิดได้เมื่อไม่มีโต๊ะที่ใช้แพ็กเกจนี้อยู่
          </span>
        </div>
        <div className="flex items-center gap-2">
          <Button type="button" variant="secondary" onClick={onCancel} disabled={saving}>
            ยกเลิก
          </Button>
          <Button type="submit" disabled={saving}>
            บันทึก
          </Button>
        </div>
      </div>

      {errors.form ? <p className="mt-2 text-[12.5px] text-cancel-ink">{errors.form}</p> : null}
    </form>
  );
}

function FormField({
  id,
  label,
  error,
  children,
}: {
  id: string;
  label: string;
  error?: string;
  children: ReactNode;
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={id}>{label}</Label>
      {children}
      {error ? <p className="text-[12.5px] text-cancel-ink">{error}</p> : null}
    </div>
  );
}

/**
 * The frame shows the ฿ inside the price boxes, so a price reads as money with no unit suffix in the
 * label. The glyph is decoration: the input's own value stays a plain number.
 */
function BahtInput({
  id,
  value,
  invalid,
  onChange,
}: {
  id: string;
  value: string;
  invalid: boolean;
  onChange: (value: string) => void;
}) {
  return (
    <div className="relative">
      <span aria-hidden className="pointer-events-none absolute inset-y-0 left-3.5 flex items-center text-ink">
        ฿
      </span>
      <Input
        id={id}
        className="tabular-nums pl-7"
        inputMode="decimal"
        value={value}
        aria-invalid={invalid}
        onChange={(event) => onChange(event.target.value)}
      />
    </div>
  );
}

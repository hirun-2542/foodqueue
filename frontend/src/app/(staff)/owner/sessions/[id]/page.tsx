import type { Metadata } from "next";
import { SessionDetailScreen } from "./session-detail-screen";

export const metadata: Metadata = {
  title: "รายละเอียดโต๊ะ · FoodQueue",
};

/**
 * Every id is a different round, so nothing here can be prerendered — the same escape hatch as
 * `/s/[token]` (Next 16 cacheComponents otherwise refuses a dynamic segment as a blocking route).
 */
export const instant = false;

/** `/owner/sessions/[id]` — frame `o04-session`: one round's orders, adjustments and itemised bill. */
export default async function SessionDetailPage({ params }: PageProps<"/owner/sessions/[id]">) {
  const { id } = await params;
  return <SessionDetailScreen sessionId={Number(id)} />;
}

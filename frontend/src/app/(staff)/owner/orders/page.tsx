import type { Metadata } from "next";
import { OrderQueueScreen } from "./order-queue-screen";

export const metadata: Metadata = {
  title: "คิวออเดอร์ · FoodQueue",
};

/** `/owner/orders` — frame o08: the owner's order queue and today's finished rounds. */
export default function OwnerOrdersPage() {
  return <OrderQueueScreen />;
}

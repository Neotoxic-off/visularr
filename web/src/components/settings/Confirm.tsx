"use client";

import type { JSX, ReactNode } from "react";
import { Alert, Button, Modal, useAction } from "@/components/ui";
import type { Action } from "@/components/ui";

// Asks before removing something; the dialog stays open with the error if the server refuses.
export function Confirm({ title, children, action, onConfirm, onClose }: {
  title: string; children: ReactNode; action: string; onConfirm: () => Promise<void>; onClose: () => void;
}): JSX.Element {
  const run: Action = useAction();
  return (
    <Modal title={title} onClose={onClose}
      footer={<>
        <Button variant="ghost" onClick={onClose}>Cancel</Button>
        <Button variant="danger" loading={run.busy} onClick={() => void run.run(async () => { await onConfirm(); onClose(); })}>{action}</Button>
      </>}>
      <div className="space-y-3 px-5 pb-5 text-body text-ink-2">
        {children}
        {run.error && <Alert>{run.error}</Alert>}
      </div>
    </Modal>
  );
}

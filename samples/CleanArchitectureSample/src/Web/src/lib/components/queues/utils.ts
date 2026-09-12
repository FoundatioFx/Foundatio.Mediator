import type { QueueSummary } from '$lib/types/queue';

export function queueLabel(queue: QueueSummary): string {
  return queue.displayName?.trim() || queue.queueName;
}

export function describeError(e: unknown): string {
  const r = e as {
    problem?: {
      title?: string;
      detail?: string;
      errors?: Record<string, string[]>;
    };
    status?: number;
    message?: string;
  };
  const fields = r?.problem?.errors
    ? Object.values(r.problem.errors).flat().join(' ')
    : '';
  return (
    fields ||
    r?.problem?.detail ||
    r?.problem?.title ||
    r?.message ||
    `Request failed${r?.status ? ` (${r.status})` : ''}`
  );
}
export function data<T>(response: { status: number; data?: T | null }): T {
  if (response.status >= 400) throw response;
  if (response.data == null)
    throw new Error('The server returned no data. Refresh to try again.');
  return response.data;
}

export const QUEUE_VIEWS = [
  'overview',
  'jobs',
  'dead-letters',
  'settings'
] as const;
export type QueueView = (typeof QUEUE_VIEWS)[number];
export function queueUrl(
  name: string,
  view: QueueView = 'overview',
  jobId?: string
): string {
  const params = new URLSearchParams();
  if (view !== 'overview') params.set('view', view);
  if (jobId) params.set('job', jobId);
  const query = params.toString();
  return `/queues/${encodeURIComponent(name)}${query ? `?${query}` : ''}`;
}

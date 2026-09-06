import {
  test,
  expect,
  type Page,
  type APIRequestContext
} from '@playwright/test';
import type {
  EnqueueReceipt,
  JobSummary,
  DeadLetterReplayResult
} from '../src/lib/types/queue';

async function signIn(page: Page) {
  await page.goto('/login?redirect=/queues');
  await page
    .getByRole('textbox', { name: 'Username', exact: true })
    .fill('admin');
  await page.getByLabel('Password', { exact: true }).fill('admin');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Enqueue export', exact: true })
  ).toBeEnabled();
}

async function job(
  request: APIRequestContext,
  id: string
): Promise<JobSummary> {
  const response = await request.get(`/api/queues/queue-job/${id}`);
  expect(response.ok()).toBeTruthy();
  return response.json();
}

async function enqueue(
  page: Page,
  button: string,
  endpoint: string
): Promise<EnqueueReceipt> {
  const response = page.waitForResponse(
    (r) =>
      r.url().endsWith(`/api/queues/enqueue/${endpoint}`) &&
      r.request().method() === 'POST'
  );
  await page.getByRole('button', { name: button, exact: true }).click();
  const result = await response;
  expect(result.ok()).toBeTruthy();
  const receipt = await result.json();
  await expect(
    page.getByText(`${receipt.count} accepted on ${receipt.queueName}`, {
      exact: true
    })
  ).toBeVisible();
  return receipt;
}

async function openJob(page: Page, id: string) {
  await page
    .getByRole('textbox', { name: 'Find job by ID', exact: true })
    .fill(id);
  await page.getByRole('button', { name: 'Open job', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Job details' })).toContainText(
    id
  );
}

async function finished(
  request: APIRequestContext,
  ids: string[],
  status = 'Completed'
) {
  await expect
    .poll(
      async () =>
        Promise.all(ids.map(async (id) => (await job(request, id)).status)),
      {
        timeout: 60_000
      }
    )
    .toEqual(ids.map(() => status));
}

test('anonymous monitoring is usable and mutations require an administrator', async ({
  page
}) => {
  const request = page.request;
  await page.goto('/queues');
  await expect(
    page.getByRole('heading', { name: 'Subscriptions', exact: true })
  ).toBeVisible();
  await expect(
    page.getByRole('button', { name: 'Enqueue export', exact: true })
  ).toBeDisabled();
  await expect(
    page.getByText('Sign in for live events', { exact: true })
  ).toBeVisible();
  const queues = await request.get('/api/queues/queues');
  expect(queues.ok()).toBeTruthy();
  expect(
    (await queues.json()).some(
      (q: { trackProgress: boolean }) => q.trackProgress
    )
  ).toBeTruthy();
  expect(
    (
      await request.post('/api/queues/enqueue/exports', { data: { count: 1 } })
    ).status()
  ).toBe(401);
});

test('running and queued jobs can be inspected, linked, and cooperatively cancelled', async ({
  page
}) => {
  const request = page.request;
  await signIn(page);
  await page.getByLabel('Export job count', { exact: true }).fill('8');
  const receipt = await enqueue(page, 'Enqueue export', 'exports');
  try {
    let running: JobSummary | undefined;
    let queued: JobSummary | undefined;
    await expect
      .poll(async () => {
        const states = await Promise.all(
          receipt.jobIds.map((id) => job(request, id))
        );
        running = states.find(
          (j) => j.status === 'Processing' && j.progress > 0
        );
        queued = states.find((j) => j.status === 'Queued');
        return !!running && !!queued;
      })
      .toBeTruthy();
    await openJob(page, running!.jobId);
    const dialog = page.getByRole('dialog', { name: 'Job details' });
    await expect(dialog).toContainText(running!.workerId!);
    await expect(dialog).toContainText('Alice Admin');
    await expect(dialog).toContainText('acme');
    await page.reload();
    await expect(dialog).toContainText(running!.jobId);
    await dialog
      .getByRole('button', { name: 'Cancel job', exact: true })
      .click();
    await expect(dialog).toContainText('Cancelled');
    await dialog.getByRole('button', { name: 'Close', exact: true }).click();
    await openJob(page, queued!.jobId);
    await dialog
      .getByRole('button', { name: 'Cancel job', exact: true })
      .click();
    await expect
      .poll(
        async () =>
          (await job(request, queued!.jobId)).cancellationRequested ||
          (await job(request, queued!.jobId)).status === 'Cancelled'
      )
      .toBeTruthy();
  } finally {
    for (const id of receipt.jobIds)
      await request.post(`/api/queues/job/${id}/cancel-job`, { data: {} });
  }
  await finished(request, receipt.jobIds, 'Cancelled');
});

test('enqueue validation rejects invalid work before returning receipts', async ({
  page
}) => {
  await signIn(page);
  await page.getByLabel('Export duration', { exact: true }).fill('0');
  const response = page.waitForResponse((r) =>
    r.url().endsWith('/api/queues/enqueue/exports')
  );
  await page
    .getByRole('button', { name: 'Enqueue export', exact: true })
    .click();
  expect((await response).status()).toBe(400);
  await expect(
    page.getByText(/StepDelayMs.*between 50 and 5000/)
  ).toBeVisible();
  await expect(page.getByRole('button', { name: /Open job ./ })).toHaveCount(0);
});

test('retry-pending work remains visible and recovers on its next attempt', async ({
  page
}) => {
  const request = page.request;
  await signIn(page);
  await page.getByLabel('Export duration', { exact: true }).fill('2');
  await page
    .getByLabel('Export outcome', { exact: true })
    .selectOption('retry');
  const receipt = await enqueue(page, 'Enqueue export', 'exports');
  await expect
    .poll(async () => (await job(request, receipt.jobIds[0])).status)
    .toBe('RetryPending');
  await page.getByRole('button', { name: 'Refresh now', exact: true }).click();
  await expect(
    page.locator(`[data-job-id="${receipt.jobIds[0]}"]`)
  ).toContainText('Waiting for retry');
  await finished(request, receipt.jobIds);
  const completed = await job(request, receipt.jobIds[0]);
  expect(completed.attempt).toBe(2);
  expect(completed.progress).toBe(100);
  expect(completed.workerId).toBeTruthy();
});

test('dead letters expose payload and original failure, then single and bulk replay return completed new jobs', async ({
  page
}) => {
  const request = page.request;
  await signIn(page);
  const first = await enqueue(page, 'Enqueue webhook', 'flaky-webhook');
  const second = await enqueue(page, 'Enqueue webhook', 'flaky-webhook');
  await finished(request, [...first.jobIds, ...second.jobIds], 'Failed');
  await page.getByRole('button', { name: 'Dead letters', exact: true }).click();
  await expect(
    page
      .getByRole('button', { name: 'Inspect DeliverWebhook', exact: true })
      .first()
  ).toBeVisible();
  await page
    .getByRole('button', { name: 'Inspect DeliverWebhook', exact: true })
    .first()
    .click();
  await expect(
    page.getByRole('heading', { name: 'Payload', exact: true })
  ).toBeVisible();
  await expect(
    page.getByRole('heading', {
      name: 'Headers and replay lineage',
      exact: true
    })
  ).toBeVisible();
  await page
    .getByRole('button', { name: 'Original job', exact: true })
    .first()
    .click();
  await expect(page.getByRole('dialog', { name: 'Job details' })).toContainText(
    'Failed'
  );
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Close', exact: true })
    .click();

  for (const button of ['Retry message', 'Retry available']) {
    const response = page.waitForResponse((r) =>
      r.url().endsWith('/dead-letters/replay')
    );
    await page
      .getByRole('button', { name: button, exact: true })
      .first()
      .click();
    const result = await response;
    expect(result.ok()).toBeTruthy();
    const replay: DeadLetterReplayResult = await result.json();
    expect(replay.replayed).toBeGreaterThan(0);
    const ids = replay.receipts.flatMap((r) => (r.jobId ? [r.jobId] : []));
    expect(
      ids.every((id) => ![...first.jobIds, ...second.jobIds].includes(id))
    ).toBeTruthy();
    await finished(request, ids);
    await expect(
      page.getByRole('button', { name: 'Refresh dead letters', exact: true })
    ).toBeEnabled();
  }
  await finished(request, [...first.jobIds, ...second.jobIds], 'Failed');
  await expect(page.getByText(/No available dead letters/)).toBeVisible();
});

test('flush asks for confirmation and preserves failed job history', async ({
  page
}) => {
  const request = page.request;
  await signIn(page);
  await page.getByLabel('Export job count', { exact: true }).fill('2');
  await page.getByLabel('Export duration', { exact: true }).fill('1');
  await page
    .getByLabel('Export outcome', { exact: true })
    .selectOption('dead-letter');
  const receipt = await enqueue(page, 'Enqueue export', 'exports');
  await finished(request, receipt.jobIds, 'Failed');
  await page.getByRole('button', { name: 'Dead letters', exact: true }).click();
  await page
    .getByRole('button', { name: 'Flush dead letters', exact: true })
    .click();
  const dialog = page.getByRole('dialog', { name: 'Flush dead letters?' });
  await expect(dialog).toContainText(receipt.queueName);
  await dialog
    .getByRole('button', { name: 'Keep messages', exact: true })
    .click();
  await expect(
    page.getByRole('button', { name: 'Inspect DemoExportJob', exact: true })
  ).toHaveCount(2);
  await page
    .getByRole('button', { name: 'Flush dead letters', exact: true })
    .click();
  await dialog
    .getByRole('button', { name: 'Delete dead letters', exact: true })
    .click();
  await expect(page.getByText(/No available dead letters/)).toBeVisible();
  await finished(request, receipt.jobIds, 'Failed');
});

test('both jobs sharing a bank lock complete and publish events across processes', async ({
  page
}) => {
  const request = page.request;
  await signIn(page);
  await expect(
    page.getByText('Event feed connected', { exact: true })
  ).toBeVisible();
  await page
    .getByLabel('Bank key', { exact: true })
    .fill(`browser-${Date.now()}`);
  const receipt = await enqueue(page, 'Enqueue 2 bank files', 'bank-files');
  await finished(request, receipt.jobIds);
  await expect(
    page.getByText('BankFileGenerated', { exact: false })
  ).toHaveCount(2);
  const states = await Promise.all(
    receipt.jobIds.map((id) => job(request, id))
  );
  expect(states.every((s) => !!s.workerId)).toBeTruthy();
});

test('pagination, queue filtering, stale-data errors, and mobile layout remain usable', async ({
  page
}) => {
  const request = page.request;
  const errors: string[] = [];
  page.on('pageerror', (error) => errors.push(error.message));
  await signIn(page);
  await page.getByLabel('Export job count', { exact: true }).fill('26');
  await page.getByLabel('Export duration', { exact: true }).fill('1');
  const receipt = await enqueue(page, 'Enqueue export', 'exports');
  await finished(request, receipt.jobIds);
  await page.getByRole('button', { name: 'All jobs', exact: true }).click();
  await expect(page.locator('[data-job-id]')).toHaveCount(25);
  const firstPage = await page
    .locator('[data-job-id]')
    .evaluateAll((rows) => rows.map((row) => row.getAttribute('data-job-id')));
  await page.getByRole('button', { name: 'Next', exact: true }).click();
  await expect
    .poll(() =>
      page.locator('[data-job-id]').first().getAttribute('data-job-id')
    )
    .not.toBe(firstPage[0]);
  const secondPage = await page
    .locator('[data-job-id]')
    .evaluateAll((rows) => rows.map((row) => row.getAttribute('data-job-id')));
  expect(secondPage.some((id) => firstPage.includes(id))).toBeFalsy();
  await page.getByLabel('Filter queues', { exact: true }).fill('exports');
  await expect(
    page
      .getByRole('table')
      .getByRole('button', { name: /ImportProductCatalog/ })
  ).toHaveCount(0);
  await page
    .getByRole('button', { name: 'Pause updates', exact: true })
    .click();
  await page.route('**/api/queues/queues', (route) =>
    route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({ title: 'Test monitoring outage', status: 503 })
    })
  );
  await page.getByRole('button', { name: 'Refresh now', exact: true }).click();
  await expect(
    page.getByText(
      /Monitoring unavailable:.*Previously loaded values may be stale/
    )
  ).toBeVisible();
  await expect(page.locator('[data-job-id]')).toHaveCount(secondPage.length);
  await page.unroute('**/api/queues/queues');
  await page.getByRole('button', { name: 'Refresh now', exact: true }).click();
  await expect(page.getByText(/Monitoring unavailable:/)).toHaveCount(0);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth
    )
  ).toBeTruthy();
  await page.screenshot({
    path: 'test-results/queue-operations-mobile.png',
    fullPage: true
  });
  expect(errors).toEqual([]);
});

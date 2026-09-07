import {
  test,
  expect,
  type Page,
  type APIRequestContext
} from '@playwright/test';
import type {
  EnqueueReceipt,
  JobSummary,
  DeadLetterReplayResult,
  QueueSummary
} from '../src/lib/types/queue';

async function signIn(page: Page) {
  await page.goto('/login?redirect=/try');
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

async function viewQueue(
  page: Page,
  receipt: EnqueueReceipt,
  view: 'jobs' | 'dead-letters' = 'jobs'
) {
  await page.getByRole('link', { name: 'View queue', exact: true }).click();
  await expect(page).toHaveURL(
    new RegExp(`/queues/${encodeURIComponent(receipt.queueName)}`)
  );
  const queue: QueueSummary = await (
    await page.request.get(
      `/api/queues/queue?queueName=${encodeURIComponent(receipt.queueName)}`
    )
  ).json();
  await expect(
    page.getByRole('heading', {
      name: queue.displayName ?? receipt.queueName,
      exact: true
    })
  ).toBeVisible();
  if (view === 'dead-letters')
    await page
      .getByRole('navigation', { name: 'Queue views' })
      .getByRole('link', { name: 'Dead letters', exact: true })
      .click();
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
  ).toHaveCount(0);
  await expect(
    page.getByRole('heading', { name: 'Try the workflow', exact: true })
  ).toHaveCount(0);
  await expect(
    page.getByRole('heading', { name: 'Live worker activity', exact: true })
  ).toHaveCount(0);
  const queues = await request.get('/api/queues/queues');
  expect(queues.ok()).toBeTruthy();
  expect(
    (await queues.json()).some(
      (q: { trackProgress: boolean }) => q.trackProgress
    )
  ).toBeTruthy();
  expect(
    (
      await request.post('/api/queues/enqueue/exports', {
        data: { count: 1 }
      })
    ).status()
  ).toBe(401);
  for (const action of ['replay', 'purge']) {
    const denied = await request.post(`/api/queues/dead-letters/${action}`, {
      data: { queueName: 'sample-DemoExportJob', messageId: 'selected-record' }
    });
    expect(denied.status()).toBe(401);
  }
  await page
    .getByRole('complementary')
    .getByRole('link', { name: 'Try it', exact: true })
    .click();
  await expect(page).toHaveURL(/\/try$/);
  await expect(
    page.getByRole('button', { name: 'Enqueue export', exact: true })
  ).toBeDisabled();
  await expect(
    page.getByRole('heading', { name: 'Subscriptions', exact: true })
  ).toHaveCount(0);
  await page
    .getByRole('main')
    .getByRole('link', { name: 'Sign in', exact: true })
    .click();
  await expect(page).toHaveURL(/redirect=\/try$/);
  await page
    .getByRole('textbox', { name: 'Username', exact: true })
    .fill('admin');
  await page.getByLabel('Password', { exact: true }).fill('admin');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(/\/try$/);
  await expect(
    page.getByRole('button', { name: 'Enqueue export', exact: true })
  ).toBeEnabled();
});

test('running and queued jobs can be inspected, linked, and cooperatively cancelled', async ({
  page
}) => {
  const request = page.request;
  await signIn(page);
  await page.getByLabel('Export job count', { exact: true }).fill('8');
  const receipt = await enqueue(page, 'Enqueue export', 'exports');
  await page
    .getByRole('link', {
      name: `Open job ${receipt.jobIds[0].slice(0, 10)}`,
      exact: true
    })
    .click();
  const receiptDialog = page.getByRole('dialog', { name: 'Job details' });
  await expect(receiptDialog).toContainText(receipt.jobIds[0]);
  await page.reload();
  await expect(receiptDialog).toContainText(receipt.jobIds[0]);
  await receiptDialog
    .getByRole('button', { name: 'Close', exact: true })
    .click();
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
      await request.post(`/api/queues/job/${id}/cancel-job`, {
        data: {}
      });
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
  await viewQueue(page, receipt);
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
  await viewQueue(page, second, 'dead-letters');
  await expect(
    page
      .getByRole('button', {
        name: 'Inspect DeliverWebhook',
        exact: true
      })
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
      page.getByRole('button', {
        name: 'Refresh dead letters',
        exact: true
      })
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
  await viewQueue(page, receipt, 'dead-letters');
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

test('individual purge removes only the selected record and individual retry preserves the original failure', async ({
  page
}) => {
  const request = page.request;
  await signIn(page);
  const first = await enqueue(page, 'Enqueue webhook', 'flaky-webhook');
  const second = await enqueue(page, 'Enqueue webhook', 'flaky-webhook');
  const originals = [...first.jobIds, ...second.jobIds];
  await finished(request, originals, 'Failed');
  await viewQueue(page, second, 'dead-letters');
  const rows = page.locator('[data-message-id]');
  await expect(rows).toHaveCount(2);
  const targetId = (await rows.first().getAttribute('data-message-id'))!;
  const otherId = (await rows.last().getAttribute('data-message-id'))!;
  const target = page.locator(`[data-message-id="${targetId}"]`);
  const other = page.locator(`[data-message-id="${otherId}"]`);
  await page.setViewportSize({ width: 390, height: 844 });
  await target
    .getByRole('button', { name: 'Purge message', exact: true })
    .click();
  const dialog = page.getByRole('dialog', { name: 'Purge this message?' });
  await expect(dialog).toContainText(targetId);
  await expect(dialog).toContainText(second.queueName);
  await expect(dialog).toContainText('Other dead letters will remain');
  await expect(dialog).not.toContainText(otherId);
  await expect
    .poll(() =>
      page.evaluate(
        () => document.documentElement.scrollWidth <= window.innerWidth
      )
    )
    .toBeTruthy();
  await dialog
    .getByRole('button', { name: 'Keep message', exact: true })
    .click();
  await expect(rows).toHaveCount(2);

  // Cancelling a selected flush must not leave its id attached to a later bulk flush.
  await page
    .getByRole('button', { name: 'Flush dead letters', exact: true })
    .click();
  const bulk = page.getByRole('dialog', { name: 'Flush dead letters?' });
  await expect(bulk).not.toContainText(targetId);
  await bulk
    .getByRole('button', { name: 'Keep messages', exact: true })
    .click();
  await target
    .getByRole('button', { name: 'Purge message', exact: true })
    .click();
  const deleted = page.waitForResponse((r) =>
    r.url().endsWith('/dead-letters/purge')
  );
  await dialog
    .getByRole('button', { name: 'Delete message', exact: true })
    .click();
  const result = await deleted;
  expect(result.ok()).toBeTruthy();
  expect(result.request().postDataJSON().messageId).toBe(targetId);
  expect((await result.json()).purged).toBe(1);
  await expect(target).toHaveCount(0);
  await expect(other).toBeVisible();
  await expect(rows).toHaveCount(1);

  // A stale record must never turn into a bulk deletion.
  const missing = await request.post('/api/queues/dead-letters/purge', {
    data: { queueName: second.queueName, messageId: targetId }
  });
  expect(missing.ok()).toBeTruthy();
  expect((await missing.json()).purged).toBe(0);
  await page
    .getByRole('button', { name: 'Refresh dead letters', exact: true })
    .click();
  await expect(
    other.getByRole('button', { name: 'Retry message', exact: true })
  ).toBeEnabled();
  const replayed = page.waitForResponse((r) =>
    r.url().endsWith('/dead-letters/replay')
  );
  await other
    .getByRole('button', { name: 'Retry message', exact: true })
    .click();
  const replayResponse = await replayed;
  expect(replayResponse.ok()).toBeTruthy();
  expect(replayResponse.request().postDataJSON().messageId).toBe(otherId);
  const replay: DeadLetterReplayResult = await replayResponse.json();
  expect(replay.replayed).toBe(1);
  const newId = replay.receipts[0].jobId!;
  expect(originals).not.toContain(newId);
  await finished(request, [newId]);
  await finished(request, originals, 'Failed');
  await expect(page.getByText(/No available dead letters/)).toBeVisible();
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
  await page
    .getByRole('link', { name: 'Open queue dashboard', exact: true })
    .click();
  await expect(
    page.getByRole('heading', { name: 'Queue dashboard', exact: true })
  ).toBeVisible();
  await expect(
    page.getByRole('heading', { name: 'Live worker activity', exact: true })
  ).toHaveCount(0);
  await expect(page.getByRole('button', { name: /^Enqueue / })).toHaveCount(0);
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
  await viewQueue(page, receipt);
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
  await page.reload();
  await expect(page).toHaveURL(/status=all.*skip=25/);
  await expect(page.locator('[data-job-id]').first()).toHaveAttribute(
    'data-job-id',
    secondPage[0]!
  );
  await page
    .getByRole('button', { name: 'Pause updates', exact: true })
    .click();
  await page.route('**/api/queues/queue?**', (route) =>
    route.fulfill({
      status: 503,
      contentType: 'application/problem+json',
      body: JSON.stringify({
        title: 'Test monitoring outage',
        status: 503
      })
    })
  );
  await page.getByRole('button', { name: 'Refresh now', exact: true }).click();
  await expect(
    page.getByText(
      /Monitoring unavailable:.*Previously loaded values may be stale/
    )
  ).toBeVisible();
  await expect(page.locator('[data-job-id]')).toHaveCount(secondPage.length);
  await page.unroute('**/api/queues/queue?**');
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
  await page
    .getByRole('link', { name: '← Back to queues', exact: true })
    .click();
  await page.getByLabel('Filter queues', { exact: true }).fill('exports');
  await expect(
    page.getByRole('table').getByRole('link', { name: /ImportProductCatalog/ })
  ).toHaveCount(0);
  expect(errors).toEqual([]);
});

test('display labels support search and navigation while queue identities stay unchanged', async ({
  page
}) => {
  const existing: QueueSummary[] = await (
    await page.request.get('/api/queues/queues')
  ).json();
  const queues = existing.slice(0, 3).map((queue, index) => ({
    ...queue,
    displayName: index < 2 ? 'Customer activity' : null
  }));
  await page.route('**/api/queues/queues', (route) =>
    route.fulfill({ json: queues })
  );
  await page.route('**/api/queues/queue?**', (route) => {
    expect(new URL(route.request().url()).searchParams.get('queueName')).toBe(
      queues[0].queueName
    );
    return route.fulfill({ json: queues[0] });
  });
  await page.goto('/queues');
  const table = page.getByRole('table', { name: 'Queue subscriptions' });
  await expect(
    table.getByRole('link', { name: 'Customer activity', exact: true })
  ).toHaveCount(2);
  await expect(
    table.getByRole('link', { name: queues[2].queueName, exact: true })
  ).toBeVisible();
  const search = page.getByLabel('Filter queues', { exact: true });
  await search.fill('CUSTOMER ACTIVITY');
  await expect(table.locator('tbody tr')).toHaveCount(2);
  await search.fill(queues[0].queueName);
  await expect(table.locator('tbody tr')).toHaveCount(1);
  await expect(
    table.getByText(queues[0].queueName, { exact: true })
  ).toBeVisible();
  const link = table.getByRole('link', {
    name: 'Customer activity',
    exact: true
  });
  await expect(link).toHaveAttribute(
    'href',
    `/queues/${encodeURIComponent(queues[0].queueName)}`
  );
  await expect(
    table.getByRole('link', {
      name: 'View dead letters for Customer activity',
      exact: true
    })
  ).toHaveAttribute(
    'href',
    `/queues/${encodeURIComponent(queues[0].queueName)}?view=dead-letters`
  );
  await link.click();
  await expect(page).toHaveURL(
    new RegExp(`/queues/${encodeURIComponent(queues[0].queueName)}$`)
  );
  await expect(
    page.getByRole('heading', { name: 'Customer activity', exact: true })
  ).toBeVisible();
  await expect(page).toHaveTitle(
    'Customer activity - Queue details - Clean Architecture Sample'
  );
  await expect(
    page.getByText(queues[0].queueName, { exact: true })
  ).toBeVisible();
  await page
    .getByRole('navigation', { name: 'Queue views' })
    .getByRole('link', { name: 'Settings', exact: true })
    .click();
  const settings = page.getByRole('region', {
    name: 'Queue settings',
    exact: true
  });
  await expect(
    settings.getByText('Customer activity', { exact: true })
  ).toBeVisible();
  await expect(
    settings.getByText(queues[0].queueName, { exact: true })
  ).toBeVisible();
  await page.reload();
  await expect(
    page.getByRole('heading', { name: 'Customer activity', exact: true })
  ).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect
    .poll(() =>
      page.evaluate(
        () => document.documentElement.scrollWidth <= window.innerWidth
      )
    )
    .toBeTruthy();
});

test('queue details isolate statistics, preserve navigation, and return to dead letters after sign-in', async ({
  page
}) => {
  const queues: QueueSummary[] = await (
    await page.request.get('/api/queues/queues')
  ).json();
  const queue = queues.find((q) => !q.trackProgress)!;
  expect(queue).toBeTruthy();
  await page.route('**/api/queues/dead-letters?**', (route) =>
    route.fulfill({
      json: [
        {
          messageId: 'access-test-message',
          messageType: queue.messageType,
          attempts: 1,
          reason: 'Test failure',
          body: '{}',
          headers: {}
        }
      ]
    })
  );
  let listReads = 0;
  let detailReads = 0;
  let deadLetterReads = 0;
  page.on('request', (request) => {
    const url = new URL(request.url());
    if (url.pathname === '/api/queues/queues') listReads++;
    if (url.pathname === '/api/queues/queue') detailReads++;
    if (url.pathname === '/api/queues/dead-letters') deadLetterReads++;
  });
  await page.route('**/api/queues/queue?**', (route) => {
    expect(new URL(route.request().url()).searchParams.get('queueName')).toBe(
      queue.queueName
    );
    return route.fulfill({
      json: {
        ...queue,
        activeCount: 17,
        inFlightCount: 8,
        delayedCount: 9,
        deadLetterCount: 2,
        messagesProcessed: 123,
        messagesFailed: 5,
        messagesDeadLettered: 2,
        counterStats: {
          totals: {
            processed: 123,
            failed: 5,
            dead_lettered: 2,
            custom_total: 19
          },
          buckets: [
            {
              hour: new Date().toISOString(),
              counters: {
                processed: 123,
                failed: 5,
                dead_lettered: 2,
                custom_total: 19
              }
            }
          ]
        }
      }
    });
  });
  await page.goto('/queues');
  await page
    .getByRole('link', {
      name: queue.displayName ?? queue.queueName,
      exact: true
    })
    .click();
  await expect(page).toHaveURL(
    new RegExp(`/queues/${encodeURIComponent(queue.queueName)}$`)
  );
  await expect(
    page.getByRole('heading', {
      name: queue.displayName ?? queue.queueName,
      exact: true
    })
  ).toBeVisible();
  await expect(
    page.getByRole('heading', { name: 'Subscriptions', exact: true })
  ).toHaveCount(0);
  await expect(
    page.getByRole('button', { name: 'Enqueue export', exact: true })
  ).toHaveCount(0);
  const transport = page.locator('[aria-label="Queue transport statistics"]');
  for (const number of ['17', '8', '9', '2'])
    await expect(transport.getByText(number, { exact: true })).toBeVisible();
  const activity = page.getByRole('region', {
    name: 'Activity in the last 24 hours'
  });
  await expect(activity.getByText('123', { exact: true })).toBeVisible();
  await expect(
    activity.getByText('custom total', { exact: true })
  ).toBeVisible();
  await expect(activity.getByText('19', { exact: true })).toBeVisible();
  await expect(
    page
      .getByRole('status', { name: 'Selected hour', exact: true })
      .getByText('123', { exact: true })
  ).toBeVisible();
  await expect(
    page.getByText(/Job tracking is not enabled for this queue/)
  ).toBeVisible();
  const dashboardReads = listReads;
  const views = page.getByRole('navigation', { name: 'Queue views' });
  await views.getByRole('link', { name: 'Settings', exact: true }).click();
  await expect(
    page.getByRole('heading', { name: 'Queue settings' })
  ).toBeVisible();
  await expect(
    page.getByText(queue.messageType, { exact: true })
  ).toBeVisible();
  for (const handler of queue.handlers)
    await expect(page.getByText(handler, { exact: true })).toBeVisible();
  await page.reload();
  await expect(
    views.getByRole('link', { name: 'Settings', exact: true })
  ).toHaveAttribute('aria-current', 'page');
  await views.getByRole('link', { name: 'Overview', exact: true }).click();
  await expect(
    views.getByRole('link', { name: 'Overview', exact: true })
  ).toHaveAttribute('aria-current', 'page');
  await page.goBack();
  await expect(
    page.getByRole('heading', { name: 'Queue settings' })
  ).toBeVisible();
  await page.goForward();
  await expect(
    page.getByRole('region', { name: 'Activity in the last 24 hours' })
  ).toBeVisible();
  expect(listReads).toBe(dashboardReads);

  await views.getByRole('link', { name: 'Dead letters', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Refresh dead letters', exact: true })
  ).toBeEnabled();
  await expect(
    page.getByRole('button', { name: 'Retry available', exact: true })
  ).toHaveCount(0);
  const letter = page.locator('[data-message-id="access-test-message"]');
  const retry = letter.getByRole('button', {
    name: 'Retry message',
    exact: true
  });
  const purge = letter.getByRole('button', {
    name: 'Purge message',
    exact: true
  });
  await expect(retry).toBeVisible();
  await expect(retry).toBeDisabled();
  await expect(purge).toBeVisible();
  await expect(purge).toBeDisabled();
  await expect(
    page.getByText(/Administrator access is required to retry or purge/)
  ).toBeVisible();
  const inspections = deadLetterReads;
  const reads = detailReads;
  await expect.poll(() => detailReads).toBeGreaterThan(reads + 1);
  expect(deadLetterReads).toBe(inspections);
  await page
    .getByRole('link', { name: 'Sign in to manage dead letters', exact: true })
    .click();
  await page
    .getByRole('textbox', { name: 'Username', exact: true })
    .fill('admin');
  await page.getByLabel('Password', { exact: true }).fill('admin');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL(
    new RegExp(
      `/queues/${encodeURIComponent(queue.queueName)}\\?view=dead-letters$`
    )
  );
  await expect(
    page.getByRole('button', { name: 'Retry available', exact: true })
  ).toBeVisible();
  await expect(retry).toBeEnabled();
  await expect(purge).toBeEnabled();
  await expect(
    page.getByText(/Administrator access is required to retry or purge/)
  ).toHaveCount(0);

  // Signing out on this view must immediately disable its actions.
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await expect(retry).toBeDisabled();
  await expect(purge).toBeDisabled();
  await page
    .getByRole('link', { name: 'Sign in to manage dead letters', exact: true })
    .click();
  await page
    .getByRole('textbox', { name: 'Username', exact: true })
    .fill('user');
  await page.getByLabel('Password', { exact: true }).fill('user');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(
    page.getByText(/Your current account has read-only access/)
  ).toBeVisible();
  await expect(retry).toBeDisabled();
  await expect(purge).toBeDisabled();
  await expect(
    page.getByRole('link', {
      name: 'Sign in to manage dead letters',
      exact: true
    })
  ).toHaveCount(0);
  for (const action of ['replay', 'purge']) {
    const denied = await page.request.post(
      `/api/queues/dead-letters/${action}`,
      {
        data: { queueName: queue.queueName, messageId: 'access-test-message' }
      }
    );
    expect(denied.status()).toBe(403);
  }
});

test('legacy queue links redirect and unavailable statistics are distinct from zero and missing queues', async ({
  page
}) => {
  await page.goto('/queues/not-a-registered-queue');
  await expect(
    page.getByText('Queue not found. It may have been removed or renamed.', {
      exact: true
    })
  ).toBeVisible();
  await expect(
    page.getByRole('navigation', { name: 'Queue views' })
  ).toHaveCount(0);
  // The sample caches queue details; cached NotFound results must remain 404s.
  for (let i = 0; i < 2; i++) {
    const response = await page.request.get(
      '/api/queues/queue?queueName=not-a-registered-queue'
    );
    expect(response.status()).toBe(404);
  }
  await page
    .getByRole('link', { name: '← Back to queues', exact: true })
    .click();
  const queues: QueueSummary[] = await (
    await page.request.get('/api/queues/queues')
  ).json();
  const queue = queues.find((q) => !q.trackProgress)!;
  await page.route('**/api/queues/queue?**', (route) =>
    route.fulfill({
      json: { ...queue, statisticsAvailable: false, counterStats: null }
    })
  );
  await page.goto(`/queues?queue=${encodeURIComponent(queue.queueName)}`);
  await expect(page).toHaveURL(
    new RegExp(`/queues/${encodeURIComponent(queue.queueName)}$`)
  );
  await expect(
    page
      .locator('[aria-label="Queue transport statistics"]')
      .getByText('—', { exact: true })
  ).toHaveCount(4);
  await expect(
    page.getByText(/Transport statistics are unavailable/)
  ).toBeVisible();
  await expect(
    page.getByRole('region', { name: 'Worker counters' })
  ).toContainText('Shared counter history is unavailable');
  await expect(
    page.getByRole('group', { name: /Hourly queue activity/ })
  ).toHaveCount(0);
});

test('dashboard filters operational issues and keeps sample work on Try it', async ({
  page
}) => {
  const actual: QueueSummary[] = await (
    await page.request.get('/api/queues/queues')
  ).json();
  const queues: QueueSummary[] = actual.slice(0, 3).map((queue, i) => ({
    ...queue,
    trackProgress: i === 2,
    statisticsAvailable: i !== 1,
    deadLetterCount: i === 0 ? 2 : i === 1 ? 999 : 0,
    inFlightCount: i === 2 ? 3 : i === 1 ? 999 : 0,
    counterStats: i === 1 ? null : queue.counterStats
  }));
  let unavailable = true;
  let reads = 0;
  let enqueues = 0;
  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/api/queues/enqueue/'))
      enqueues++;
  });
  await page.route('**/api/queues/queues', (route) => {
    reads++;
    return unavailable
      ? route.fulfill({
          status: 503,
          contentType: 'application/problem+json',
          body: JSON.stringify({ title: 'Monitoring outage', status: 503 })
        })
      : route.fulfill({ json: queues });
  });
  await page.goto('/queues');
  await expect(page.getByText(/Queue data is unavailable/)).toBeVisible();
  await expect(
    page
      .getByRole('group', { name: 'Queue transport totals' })
      .getByText('—', { exact: true })
  ).toHaveCount(4);
  await expect(
    page.getByText('No queues are registered.', { exact: true })
  ).toHaveCount(0);
  unavailable = false;
  await page.getByRole('button', { name: 'Refresh now', exact: true }).click();
  const table = page.getByRole('table', { name: 'Queue subscriptions' });
  await expect(table.locator('tbody tr')).toHaveCount(3);
  const view = page.getByRole('combobox', { name: 'Queue view', exact: true });
  for (const [value, index] of [
    ['dead-letters', 0],
    ['in-flight', 2],
    ['unavailable', 1],
    ['tracked', 2]
  ] as const) {
    await view.selectOption(value);
    await expect(table.locator('tbody tr')).toHaveCount(1);
    await expect(
      table.getByRole('link', {
        name: queues[index].displayName ?? queues[index].queueName,
        exact: true
      })
    ).toBeVisible();
  }
  await expect(
    table.getByRole('link', {
      name: `View dead letters for ${queues[2].displayName ?? queues[2].queueName}`,
      exact: true
    })
  ).toHaveAttribute(
    'href',
    `/queues/${encodeURIComponent(queues[2].queueName)}?view=dead-letters`
  );
  await view.selectOption('all');
  const unknown = table.getByRole('row').filter({
    has: page.getByRole('link', {
      name: queues[1].displayName ?? queues[1].queueName,
      exact: true
    })
  });
  await expect(unknown.getByText('—', { exact: true })).toHaveCount(6);
  await page
    .getByLabel('Filter queues', { exact: true })
    .fill('no-such-subscription');
  await expect(
    page.getByText('No queues match your filter.', { exact: true })
  ).toBeVisible();
  await page.getByLabel('Filter queues', { exact: true }).fill('');
  await page
    .getByRole('button', { name: 'Pause updates', exact: true })
    .click();
  unavailable = true;
  await page.getByRole('button', { name: 'Refresh now', exact: true }).click();
  await expect(
    page.getByText(
      /Monitoring unavailable:.*Previously loaded values may be stale/
    )
  ).toBeVisible();
  await expect(table.locator('tbody tr')).toHaveCount(3);
  await expect(
    page
      .getByRole('main')
      .getByText(/scenario|sample jobs|bank files|Try the workflow/i)
  ).toHaveCount(0);
  expect(enqueues).toBe(0);

  await page
    .getByRole('complementary')
    .getByRole('link', { name: 'Try it', exact: true })
    .click();
  await expect(
    page.getByRole('heading', { name: 'Try it', exact: true })
  ).toBeVisible();
  await page.reload();
  await expect(
    page.getByRole('heading', { name: 'Try the workflow', exact: true })
  ).toBeVisible();
  const before = reads;
  await page.clock.install();
  await page.clock.runFor(6000);
  expect(reads).toBe(before);
  await page.setViewportSize({ width: 390, height: 844 });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth
    )
  ).toBeTruthy();
  await expect(
    page
      .getByRole('navigation', { name: 'Sample navigation' })
      .getByRole('link', { name: 'Try it', exact: true })
  ).toBeVisible();
  await page.screenshot({
    path: 'test-results/try-workflow-mobile.png',
    fullPage: true
  });
  await page
    .getByRole('link', { name: 'Open queue dashboard', exact: true })
    .click();
  await expect(
    page.getByRole('heading', { name: 'Queue dashboard', exact: true })
  ).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth
    )
  ).toBeTruthy();
});

test('hourly histogram shows three distinct series with pointer and keyboard inspection', async ({
  page
}) => {
  const actual: QueueSummary[] = await (
    await page.request.get('/api/queues/queues')
  ).json();
  const queue = actual.find((q) => !q.trackProgress)!;
  const end = Math.floor(Date.now() / 3_600_000) * 3_600_000;
  const buckets = Array.from({ length: 24 }, (_, i) => ({
    hour: new Date(end - (23 - i) * 3_600_000).toISOString(),
    counters: {
      processed: i === 23 ? 28 : (i * 7) % 23,
      failed: i === 8 ? 5 : i === 23 ? 1 : 0,
      dead_lettered: i === 16 ? 7 : i === 23 ? 2 : 0
    }
  }));
  let mode = 'normal';
  await page.route('**/api/queues/queue?**', (route) =>
    route.fulfill({
      json: {
        ...queue,
        counterStats: {
          totals: { processed: 250, failed: 6, dead_lettered: 9 },
          buckets:
            mode === 'empty'
              ? []
              : buckets
                  .map((bucket) => ({
                    ...bucket,
                    counters:
                      mode === 'zero'
                        ? {}
                        : mode === 'large'
                          ? { ...bucket.counters, processed: 2_000_000 }
                          : bucket.counters
                  }))
                  .toReversed()
        }
      }
    })
  );
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await page.goto(`/queues/${encodeURIComponent(queue.queueName)}`);
  const chart = page.getByRole('group', {
    name: `Hourly queue activity for ${queue.queueName}`,
    exact: true
  });
  await expect(chart).toBeVisible();
  await expect(page.getByRole('table', { name: /Hourly/ })).toHaveCount(0);
  await expect(chart.locator('path[data-series]')).toHaveCount(3);
  const paths = await chart
    .locator('path[data-series]')
    .evaluateAll((nodes) =>
      nodes.map((node) => ({
        series: node.getAttribute('data-series'),
        color: node.getAttribute('stroke'),
        dash: node.getAttribute('stroke-dasharray'),
        path: node.getAttribute('d')
      }))
    );
  expect(new Set(paths.map((path) => path.color)).size).toBe(3);
  expect(new Set(paths.map((path) => path.dash)).size).toBe(3);
  expect(new Set(paths.map((path) => path.path)).size).toBe(3);
  const values = page.getByRole('status', {
    name: 'Selected hour',
    exact: true
  });
  await expect(values).toHaveText(
    /Completed\s*28.*Failed attempts\s*1.*Dead-lettered\s*2/
  );
  const hours = chart.getByRole('button');
  await expect(hours).toHaveCount(24);
  await hours.nth(8).hover();
  await expect(values).toHaveText(/Failed attempts\s*5/);
  await hours.first().focus();
  await page.keyboard.press('ArrowRight');
  await expect(hours.nth(1)).toBeFocused();
  await expect(values).toHaveText(/Completed\s*7/);
  await page.keyboard.press('End');
  await expect(hours.last()).toBeFocused();
  await expect(values).toHaveText(/Completed\s*28/);
  await page.screenshot({
    path: 'test-results/queue-hourly-histogram.png',
    fullPage: true
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await hours.nth(16).click();
  await expect(values).toHaveText(/Dead-lettered\s*7/);
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth
    )
  ).toBeTruthy();
  await page.screenshot({
    path: 'test-results/queue-hourly-histogram-mobile.png',
    fullPage: true
  });
  await page
    .getByRole('button', { name: 'Pause updates', exact: true })
    .click();
  mode = 'large';
  await page.getByRole('button', { name: 'Refresh now', exact: true }).click();
  await expect(values).toHaveText(/Completed\s*2,000,000/);
  for (const name of ['large', 'zero']) {
    mode = name;
    await page
      .getByRole('button', { name: 'Refresh now', exact: true })
      .click();
    if (name === 'zero')
      await expect(values).toHaveText(
        /Completed\s*0.*Failed attempts\s*0.*Dead-lettered\s*0/
      );
    expect(
      await chart.evaluate((svg) =>
        Array.from(
          svg.querySelectorAll<SVGPathElement>('path[data-series]')
        ).every((path) => {
          const bounds = path.getBBox();
          const view = (svg as SVGSVGElement).viewBox.baseVal;
          return (
            [bounds.x, bounds.y, bounds.width, bounds.height].every(
              Number.isFinite
            ) &&
            bounds.x >= 0 &&
            bounds.y >= 0 &&
            bounds.x + bounds.width <= view.width &&
            bounds.y + bounds.height <= view.height
          );
        })
      )
    ).toBeTruthy();
  }
  mode = 'empty';
  await page.getByRole('button', { name: 'Refresh now', exact: true }).click();
  await expect(
    page.getByText('No hourly activity has been recorded in this window.', {
      exact: true
    })
  ).toBeVisible();
  await expect(chart).toHaveCount(0);
  expect(errors).toEqual([]);
});

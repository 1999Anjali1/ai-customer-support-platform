import { Browser, Page, expect, test } from '@playwright/test';

/**
 * Full journey across three roles, using the real stack:
 * customer chats with the AI → asks for a human → agent receives it live, replies →
 * customer sees the agent's reply in real time (SignalR) → admin checks analytics & knowledge base.
 */

const shots = 'screenshots';
/** True when the workflow has a real LLM key (GROQ_API_KEY secret); otherwise the AI runs in demo mode. */
const aiEnabled = process.env['AI_ENABLED'] === 'true';

async function login(browser: Browser, name: string): Promise<Page> {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto('/login');
  await page.getByRole('button', { name: new RegExp(name) }).click();
  await expect(page).not.toHaveURL(/login/);
  return page;
}

test('customer → AI → human agent hand-over in real time', async ({ browser }) => {
  // ---- customer
  const customer = await login(browser, 'Rahul Nair');
  await expect(customer.locator('.msg').first()).toContainText('SmartSupport');
  await expect(customer.locator('.conn.connected')).toBeVisible();

  const question = "I'm travelling to Saudi Arabia next week. Can you turn on roaming for me? Which pack should I take?";
  await customer.getByPlaceholder(/Type your message/).fill(question);
  await customer.getByRole('button', { name: 'Send' }).click();

  // the customer's message, then an AI reply (real model if GROQ_API_KEY is set, otherwise demo-mode text)
  await expect(customer.locator('.msg.mine')).toContainText('Saudi Arabia');
  await expect(customer.locator('.msg:not(.mine) .text')).toHaveCount(2, { timeout: 60_000 });
  await expect(customer.locator('.bubble.thinking')).toHaveCount(0, { timeout: 60_000 });
  await expect(customer.locator('.cursor')).toHaveCount(0, { timeout: 60_000 });
  await customer.screenshot({ path: `${shots}/1-customer-ai-chat.png` });

  await customer.getByRole('button', { name: /Talk to a human/ }).click();
  await expect(customer.locator('.system-note')).toContainText(/human support agent/i);

  // ---- agent sees the escalation in the queue and joins
  const agent = await login(browser, 'Sara Thomas');
  await expect(agent).toHaveURL(/agent/);
  const item = agent.locator('.queue-item', { hasText: 'Rahul Nair' });
  await expect(item).toBeVisible();
  await item.click();
  await agent.getByRole('button', { name: 'Accept chat' }).click();
  await expect(agent.locator('.chat-header')).toContainText('In progress');

  if (aiEnabled) {
    await agent.getByRole('button', { name: 'Generate' }).click();
    await expect(agent.locator('.copilot .md')).toContainText(/Issue/i, { timeout: 60_000 });
    await agent.getByRole('button', { name: 'Suggest' }).click();
    await expect(agent.locator('.suggestion')).toBeVisible({ timeout: 60_000 });
  }

  await agent.getByPlaceholder(/Write a reply/).fill('Hi Rahul, Sara here from the support team. Let me check that for you.');
  await agent.getByRole('button', { name: 'Send' }).click();

  // ---- customer receives the agent reply live (no page refresh)
  await expect(customer.locator('.msg.agent')).toContainText('Sara here', { timeout: 15_000 });
  await expect(customer.locator('.status-line')).toContainText('Sara Thomas');
  await customer.screenshot({ path: `${shots}/2-customer-with-agent.png` });
  await agent.screenshot({ path: `${shots}/3-agent-console.png` });

  await agent.getByRole('button', { name: /Resolve/ }).click();
  await expect(customer.locator('.status-line')).toContainText(/closed/i);
});

test('admin sees analytics and can search the knowledge base', async ({ browser }) => {
  const admin = await login(browser, 'Omar Haddad');
  await expect(admin).toHaveURL(/analytics/);
  await expect(admin.locator('.kpi.highlight')).toContainText('%');
  await admin.waitForTimeout(800); // chart animation
  await admin.screenshot({ path: `${shots}/4-analytics.png`, fullPage: true });

  await admin.getByRole('link', { name: /Knowledge base/ }).click();
  await expect(admin.locator('tbody tr')).toHaveCount(10);
  await admin.locator('input[name=q]').fill('roaming pack for Saudi Arabia');
  await admin.getByRole('button', { name: 'Search', exact: true }).click();
  await expect(admin.locator('.hit').first()).toContainText('Roaming');
  await admin.screenshot({ path: `${shots}/5-knowledge-base.png` });

  await admin.getByRole('link', { name: /Tickets/ }).click();
  await expect(admin.locator('tbody tr').first()).toContainText('TCK-');
});

test('Arabic customer gets a right-to-left interface', async ({ browser }) => {
  const customer = await login(browser, 'Fatima Al-Marri');
  await expect(customer.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(customer.locator('.msg').first()).toContainText('مرحباً');
  await customer.screenshot({ path: `${shots}/6-arabic-rtl.png` });
});

test('login page', async ({ page }) => {
  await page.goto('/login');
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
  await page.screenshot({ path: `${shots}/0-login.png` });
});

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace MonitorBrowser107;

// FIX (сессия 1 из 2, тема: "рукопожатие"):
// 4.084 заменил рабочую физическую эмуляцию клика мышью (SetCursorPos + mouse_event)
// на попытку сфокусировать поле ввода чисто через UI Automation (ValuePattern.SetValue /
// AutomationElement.SetFocus) плюс блокирующий Thread.Sleep(700) на потоке UI.
// На реальной странице ChatGPT/Claude/Gemini (contenteditable-редактор на ProseMirror)
// это ненадёжно: сайт часто не принимает такую "программную" фокусировку как настоящий
// пользовательский ввод, а Thread.Sleep замораживает цикл сообщений WinForms как раз
// тогда, когда WebView2 должен обработать смену фокуса.
//
// Этот файл — точный откат к схеме из FPR_4.015, которая была подтверждена рабочей:
// физический клик мышью в вычисленную "безопасную зону" композера, затем Ctrl+A/Ctrl+V
// через системные события клавиатуры, с проверкой результата через выделение+копирование.
// Enter никогда не нажимается — отправку сообщения всегда выполняет сам Pilot/Owner.
internal static class ChatPromptWriter
{
	private const uint MouseEventLeftDown = 0x0002;
	private const uint MouseEventLeftUp = 0x0004;
	private const uint KeyEventKeyUp = 0x0002;
	private const byte VirtualKeyControl = 0x11;
	private const byte VirtualKeyA = 0x41;
	private const byte VirtualKeyV = 0x56;
	private const byte VirtualKeyDelete = 0x2E;

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetCursorPos(out NativePoint point);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll")]
	private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extraInfo);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetForegroundWindow(nint windowHandle);

	[DllImport("user32.dll")]
	private static extern nint SetFocus(nint windowHandle);

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

	internal static async Task<ChatPromptWriteResult> WriteAsync(WebView2? webView, string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return ChatPromptWriteResult.Failed("Тело FPR не вернуло текст протокола.");
		}

		if (webView?.CoreWebView2 is null || !webView.IsHandleCreated)
		{
			return ChatPromptWriteResult.Failed("Страница чата не готова.");
		}

		try
		{
			// Никогда не обходим дерево UI Automation браузера на потоке UI WinForms.
			// На длинном диалоге ChatGPT синхронный FindAll может заблокировать
			// обработку сообщений Windows и превратить Monitor в "Не отвечает".
			//
			// Ограниченный физический маршрут использует только видимые границы
			// WebView, буфер обмена и события клавиатуры/мыши. Enter не нажимается.
			if (!await SetClipboardTextAsync(text))
			{
				return ChatPromptWriteResult.Failed("Не удалось подготовить текст FPR в буфере обмена.");
			}

			return await WriteThroughPhysicalPromptAsync(webView, text);
		}
		catch (Exception ex)
		{
			return ChatPromptWriteResult.Failed("Не удалось вставить текст FPR в чат: " + ex.Message);
		}
	}

	private static async Task<bool> SetClipboardTextAsync(string text)
	{
		for (int attempt = 0; attempt < 6; attempt++)
		{
			try
			{
				Clipboard.SetText(text, TextDataFormat.UnicodeText);
				return true;
			}
			catch (ExternalException)
			{
				await Task.Delay(80);
			}
		}

		return false;
	}

	private static async Task<ChatPromptWriteResult> WriteThroughPhysicalPromptAsync(WebView2 webView, string text)
	{
		Rectangle bounds = webView.RectangleToScreen(webView.ClientRectangle);

		if (bounds.Width < 200 || bounds.Height < 160)
		{
			return ChatPromptWriteResult.Failed("Область чата слишком мала для безопасной вставки.");
		}

		Form? ownerForm = webView.FindForm();
		if (ownerForm is not null && ownerForm.IsHandleCreated)
		{
			SetForegroundWindow(ownerForm.Handle);
			ownerForm.Activate();
		}

		webView.Focus();
		SetFocus(webView.Handle);

		// Стабильная зона композера для поддерживаемых чатов: нижняя средняя
		// часть WebView, в стороне от боковых панелей и кнопок отправки/голоса.
		int x = bounds.Left + (int)Math.Round(bounds.Width * 0.62);
		int verticalInset = Math.Clamp((int)Math.Round(bounds.Height * 0.055), 38, 64);
		int y = bounds.Bottom - verticalInset;

		bool cursorCaptured = GetCursorPos(out NativePoint originalPoint);

		try
		{
			for (int attempt = 0; attempt < 3; attempt++)
			{
				if (!SetCursorPos(x, y))
				{
					return ChatPromptWriteResult.Failed("Не удалось сфокусировать строку ввода чата.");
				}

				await Task.Delay(90);
				mouse_event(MouseEventLeftDown, 0, 0, 0, 0);
				mouse_event(MouseEventLeftUp, 0, 0, 0, 0);
				await Task.Delay(120);

				// Явно зачищаем поле перед вставкой: выделить всё, удалить. Так
				// повтор никогда не наслаивается на недовставленный или неудачно
				// проверенный остаток предыдущей попытки. Enter не нажимается.
				SendControlShortcut(VirtualKeyA);
				await Task.Delay(200);
				SendKeyPress(VirtualKeyDelete);
				await Task.Delay(200);

				if (!await SetClipboardTextAsync(text))
				{
					return ChatPromptWriteResult.Failed("Не удалось повторно подготовить текст FPR в буфере обмена.");
				}
				SendControlShortcut(VirtualKeyV);

				// FIX (по замечанию: нельзя зашивать фиксированный потолок ожидания).
				// Текст рукопожатия не имеет верхней границы размера — сегодня это
				// десятки КБ BHV, завтра может быть файл, который браузер обрабатывает
				// минуту. Поэтому не гадаем заранее, сколько ждать: реально опрашиваем
				// поле, пока оно не совпадёт с ожидаемым текстом. Ждём столько, сколько
				// поле продолжает меняться (расти); останавливаемся раньше только если
				// рост явно застрял (перестал меняться) — тогда это не "ещё рисуется",
				// а реальный промах клика или иной сбой, и есть смысл повторить попытку
				// с начала, а не ждать дальше то, что не изменится.
				if (await WaitForComposerToMatchAsync(text))
				{
					await SetClipboardTextAsync(text);
					return ChatPromptWriteResult.Completed();
				}

				// Клик мог случиться до того, как композер закончил рендер, или
				// проверка застала ещё недорисованный текст. Восстанавливаем
				// payload в буфере; следующая попытка зачистит поле заново перед
				// повторной вставкой — наслоения не будет.
				await SetClipboardTextAsync(text);
				await Task.Delay(300);
			}

			return ChatPromptWriteResult.Failed(
				"Текст FPR подготовлен в буфере, но безопасная вставка в строку чата не подтверждена.");
		}
		finally
		{
			if (cursorCaptured)
			{
				SetCursorPos(originalPoint.X, originalPoint.Y);
			}
		}
	}

	// Опрашивает поле чата, пока вставленный текст не совпадёт с ожидаемым.
	// Без искусственного потолка "на глаз" под текущий размер — ждём столько,
	// сколько поле продолжает меняться. Останавливаемся раньше только тогда,
	// когда рост явно застрял (несколько проверок подряд без изменений) —
	// это признак реального сбоя (промах клика, фокус ушёл), а не медленной
	// отрисовки, и тогда лучше сразу повторить попытку с начала, чем ждать
	// впустую то, что дальше не изменится.
	private static async Task<bool> WaitForComposerToMatchAsync(string expected)
	{
		const int pollIntervalMs = 250;
		const int maxTotalWaitMs = 10 * 60 * 1000; // с большим запасом на будущие большие файлы
		const int stallTimeoutMs = 5000;

		int elapsed = 0;
		int stalledFor = 0;
		int lastLength = -1;

		while (elapsed < maxTotalWaitMs)
		{
			SendControlShortcut(VirtualKeyA);
			await Task.Delay(80);
			SendControlShortcut(0x43); // C
			await Task.Delay(80);
			elapsed += 160;

			if (TryReadClipboardText(out string copied))
			{
				if (TextMatches(copied, expected))
				{
					return true;
				}

				int length = NormalizeText(copied).Length;
				if (length == lastLength)
				{
					stalledFor += pollIntervalMs;
					if (stalledFor >= stallTimeoutMs)
					{
						return false;
					}
				}
				else
				{
					stalledFor = 0;
					lastLength = length;
				}
			}

			await Task.Delay(pollIntervalMs);
			elapsed += pollIntervalMs;
		}

		return false;
	}

	private static bool TryReadClipboardText(out string text)
	{
		text = string.Empty;

		try
		{
			if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
			{
				return false;
			}

			text = Clipboard.GetText(TextDataFormat.UnicodeText);
			return true;
		}
		catch (ExternalException)
		{
			return false;
		}
	}

	private static void SendControlShortcut(byte virtualKey)
	{
		keybd_event(VirtualKeyControl, 0, 0, 0);
		keybd_event(virtualKey, 0, 0, 0);
		keybd_event(virtualKey, 0, KeyEventKeyUp, 0);
		keybd_event(VirtualKeyControl, 0, KeyEventKeyUp, 0);
	}

	private static void SendKeyPress(byte virtualKey)
	{
		keybd_event(virtualKey, 0, 0, 0);
		keybd_event(virtualKey, 0, KeyEventKeyUp, 0);
	}

	private static bool TextMatches(string actualText, string expectedText)
	{
		string actual = NormalizeText(actualText);
		string expected = NormalizeText(expectedText);

		if (actual.Length == 0 || expected.Length == 0)
		{
			return false;
		}

		return
			string.Equals(actual, expected, StringComparison.Ordinal) ||
			actual.Contains(expected, StringComparison.Ordinal);
	}

	private static string NormalizeText(string value) =>
		value
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace('\r', '\n')
			.Replace("\u200B", string.Empty, StringComparison.Ordinal)
			.Trim();

	[StructLayout(LayoutKind.Sequential)]
	private struct NativePoint
	{
		internal int X;
		internal int Y;
	}
}

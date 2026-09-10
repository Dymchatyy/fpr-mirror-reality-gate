using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace MonitorBrowser107;

// FIX (сессия 2 из 2, тема: "считать ответ"):
// 4.084 переписал FindLowestVisibleCopyButton так, что он больше не проверяет
// пересечение кнопки с видимой областью WebView и не выбирает самую нижнюю
// (=последнюю по времени) кнопку "Копировать" — вместо этого он просто
// перезаписывает результат на каждой найденной подходящей кнопке и возвращает
// последнюю встреченную в порядке обхода дерева UI Automation, что не связано
// с порядком сообщений на экране. Плюс ClickAutomationElementAsync лишился
// резервного физического клика мышью: если у кнопки нет InvokePattern, клика
// не происходит вовсе.
//
// Этот файл восстанавливает рабочую схему 4.015: кнопка ищется среди дескрипторов
// WebView и родительской формы, обязана пересекаться с видимыми границами WebView,
// а из всех подходящих выбирается та, что физически ниже всех на экране (эвристика
// "последний ответ — внизу ленты"). Клик пробует InvokePattern, а при его
// отсутствии — физический SetCursorPos + mouse_event по центру кнопки.
internal static class ChatResponseTransfer
{
	private const string ChatNotReady = "Страница чата не готова.";
	private const string CopyButtonNotFound = "Кнопка «Копировать» последнего ответа не найдена.";
	private const string TextNotReceived = "Текст не получен.";

	private const uint MouseEventLeftDown = 0x0002;
	private const uint MouseEventLeftUp = 0x0004;

	[DllImport("user32.dll")]
	private static extern uint GetClipboardSequenceNumber();

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetCursorPos(out NativePoint point);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetCursorPos(int x, int y);

	[DllImport("user32.dll")]
	private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extraInfo);

	internal static async Task<string> CaptureLastResponseAsync(WebView2? webView)
	{
		if (webView?.CoreWebView2 is null || !webView.IsHandleCreated)
		{
			return ChatNotReady;
		}

		try
		{
			AutomationElement? copyButton = await FindLowestVisibleCopyButtonBoundedAsync(webView);

			if (copyButton is null)
			{
				return CopyButtonNotFound;
			}

			uint sequenceBeforeClick = GetClipboardSequenceNumber();

			if (!await ClickAutomationElementAsync(copyButton))
			{
				return CopyButtonNotFound;
			}

			for (int attempt = 0; attempt < 30; attempt++)
			{
				await Task.Delay(100);

				if (GetClipboardSequenceNumber() == sequenceBeforeClick)
				{
					continue;
				}

				if (!TryReadClipboardText(out string clipboardText))
				{
					continue;
				}

				if (string.IsNullOrWhiteSpace(clipboardText))
				{
					continue;
				}

				return clipboardText;
			}

			return TextNotReceived;
		}
		catch
		{
			return TextNotReceived;
		}
	}

	private static async Task<AutomationElement?> FindLowestVisibleCopyButtonBoundedAsync(WebView2 webView)
	{
		Point webViewTopLeft = webView.PointToScreen(Point.Empty);
		Rectangle webViewBounds = new(webViewTopLeft, webView.ClientSize);
		nint webViewHandle = webView.Handle;
		Form? ownerForm = webView.FindForm();
		nint ownerHandle = ownerForm is not null && ownerForm.IsHandleCreated ? ownerForm.Handle : 0;

		Task<AutomationElement?> search = Task.Run(() =>
			FindLowestVisibleCopyButton(webViewHandle, ownerHandle, webViewBounds));
		Task completed = await Task.WhenAny(search, Task.Delay(TimeSpan.FromSeconds(6)));
		if (!ReferenceEquals(completed, search))
		{
			_ = search.ContinueWith(
				task => _ = task.Exception,
				TaskContinuationOptions.OnlyOnFaulted);
			return null;
		}

		try
		{
			return await search;
		}
		catch
		{
			return null;
		}
	}

	private static AutomationElement? FindLowestVisibleCopyButton(
		nint webViewHandle,
		nint ownerHandle,
		Rectangle webViewBounds)
	{
		List<AutomationElement> roots = new();
		TryAddAutomationRoot(roots, webViewHandle);

		if (ownerHandle != 0)
		{
			TryAddAutomationRoot(roots, ownerHandle);
		}

		AutomationElement? lowestButton = null;
		double lowestBottom = double.MinValue;

		System.Windows.Automation.Condition buttonCondition =
			new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);

		foreach (AutomationElement root in roots)
		{
			AutomationElementCollection buttons;

			try
			{
				buttons = root.FindAll(TreeScope.Descendants, buttonCondition);
			}
			catch
			{
				continue;
			}

			foreach (AutomationElement button in buttons)
			{
				try
				{
					AutomationElement.AutomationElementInformation current = button.Current;

					if (!current.IsEnabled || current.IsOffscreen)
					{
						continue;
					}

					string name = current.Name?.Trim() ?? string.Empty;

					if (!IsCopyButtonName(name))
					{
						continue;
					}

					var bounds = current.BoundingRectangle;

					if (bounds.IsEmpty || bounds.Width <= 1 || bounds.Height <= 1)
					{
						continue;
					}

					Rectangle candidateBounds = Rectangle.FromLTRB(
						(int)Math.Floor(bounds.Left),
						(int)Math.Floor(bounds.Top),
						(int)Math.Ceiling(bounds.Right),
						(int)Math.Ceiling(bounds.Bottom));

					if (!webViewBounds.IntersectsWith(candidateBounds))
					{
						continue;
					}

					if (bounds.Bottom > lowestBottom)
					{
						lowestBottom = bounds.Bottom;
						lowestButton = button;
					}
				}
				catch
				{
					// Элемент мог исчезнуть при обновлении страницы.
				}
			}

			if (lowestButton is not null)
			{
				break;
			}
		}

		return lowestButton;
	}

	private static void TryAddAutomationRoot(ICollection<AutomationElement> roots, nint handle)
	{
		try
		{
			AutomationElement root = AutomationElement.FromHandle(handle);

			if (!Contains(roots, root))
			{
				roots.Add(root);
			}
		}
		catch
		{
			// Корень недоступен — пробуем следующий.
		}
	}

	private static bool Contains(ICollection<AutomationElement> roots, AutomationElement candidate)
	{
		foreach (AutomationElement existing in roots)
		{
			if (existing.Equals(candidate))
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsCopyButtonName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		return
			name.Equals("Копировать", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("Скопировать", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("Copy", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("Копировать ", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("Скопировать ", StringComparison.OrdinalIgnoreCase) ||
			name.StartsWith("Copy ", StringComparison.OrdinalIgnoreCase);
	}

	private static async Task<bool> ClickAutomationElementAsync(AutomationElement element)
	{
		var bounds = element.Current.BoundingRectangle;

		try
		{
			if (element.TryGetCurrentPattern(InvokePattern.Pattern, out object? patternObject) &&
				patternObject is InvokePattern invokePattern)
			{
				invokePattern.Invoke();
				await Task.Delay(120);
				return true;
			}
		}
		catch
		{
			// Узел мог быть заменён при перерисовке страницы.
			// Ниже остаётся резервный физический клик.
		}

		if (bounds.IsEmpty || bounds.Width <= 1 || bounds.Height <= 1)
		{
			return false;
		}

		int x = (int)Math.Round(bounds.Left + (bounds.Width / 2));
		int y = (int)Math.Round(bounds.Top + (bounds.Height / 2));

		bool cursorCaptured = GetCursorPos(out NativePoint originalPoint);

		try
		{
			if (!SetCursorPos(x, y))
			{
				return false;
			}

			await Task.Delay(80);
			mouse_event(MouseEventLeftDown, 0, 0, 0, 0);
			mouse_event(MouseEventLeftUp, 0, 0, 0, 0);
			await Task.Delay(120);
			return true;
		}
		finally
		{
			if (cursorCaptured)
			{
				SetCursorPos(originalPoint.X, originalPoint.Y);
			}
		}
	}

	private static bool TryReadClipboardText(out string text)
	{
		try
		{
			if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
			{
				text = string.Empty;
				return false;
			}

			text = Clipboard.GetText(TextDataFormat.UnicodeText);
			return true;
		}
		catch
		{
			text = string.Empty;
			return false;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativePoint
	{
		internal int X;
		internal int Y;
	}
}

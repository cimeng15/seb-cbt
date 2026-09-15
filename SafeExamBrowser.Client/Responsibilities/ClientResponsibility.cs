/*
 * Copyright (c) 2026 ETH Zürich, IT Services
 * 
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using System;
using System.Collections.Generic;
using SafeExamBrowser.Core.Contracts.ResponsibilityModel;
using SafeExamBrowser.I18n.Contracts;
using SafeExamBrowser.Logging.Contracts;
using SafeExamBrowser.Settings;
using SafeExamBrowser.UserInterface.Contracts.MessageBox;
using SafeExamBrowser.UserInterface.Contracts.Windows.Data;

namespace SafeExamBrowser.Client.Responsibilities
{
	internal abstract class ClientResponsibility : IResponsibility<ClientTask>
	{
		protected ClientContext Context { get; private set; }
		protected ILogger Logger { get; private set; }

		protected AppSettings Settings => Context.Settings;

		internal ClientResponsibility(ClientContext context, ILogger logger)
		{
			Context = context;
			Logger = logger;
		}

		public abstract void Assume(ClientTask task);

		protected void PauseActivators()
		{
			foreach (var activator in Context.Activators)
			{
				activator.Pause();
			}
		}

		/// <summary>
		/// Determines whether a quit/unlock password is required, either because a quit password hash or a CBT kiosk settings URL is configured.
		/// </summary>
		protected bool HasQuitPassword => !string.IsNullOrEmpty(Settings?.Security?.QuitPasswordHash) || !string.IsNullOrEmpty(Settings?.Security?.CbtKioskUrl);

		protected bool IsValidQuitPassword(string password)
		{
			var valid = VerifyQuitPassword(password) == QuitPasswordVerification.Valid;

			if (valid)
			{
				Context.QuitPasswordValidated = true;
			}

			return valid;
		}

		/// <summary>
		/// Verifies the given quit/unlock password, either against the CBT kiosk settings endpoint (if configured) or against the locally
		/// configured quit password hash. Returns a detailed result so that the caller can distinguish a wrong password from an expired one.
		/// </summary>
		protected QuitPasswordVerification VerifyQuitPassword(string password)
		{
			if (!string.IsNullOrEmpty(Settings.Security.CbtKioskUrl) && Context.CbtKioskClient != default)
			{
				return VerifyAgainstCbtKiosk(password);
			}

			return IsValidLocalQuitPassword(password) ? QuitPasswordVerification.Valid : QuitPasswordVerification.Invalid;
		}

		/// <summary>
		/// Validates the given password against the locally configured quit password hash.
		/// </summary>
		private bool IsValidLocalQuitPassword(string password)
		{
			var actual = Context.HashAlgorithm.GenerateHashFor(password);
			var expected = Settings.Security.QuitPasswordHash;

			return expected.Equals(actual, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Validates the given password against the value currently configured on the CBT kiosk settings endpoint. If the primary endpoint
		/// cannot be reached, the configured fallback endpoint is queried (online fallback). Only when neither endpoint is reachable does
		/// the locally configured quit password hash apply (if any), so that an exam can always be terminated even when the CBT server is
		/// unavailable.
		/// </summary>
		private QuitPasswordVerification VerifyAgainstCbtKiosk(string password)
		{
			var attempts = Settings.Security.CbtKioskAttempts > 0 ? Settings.Security.CbtKioskAttempts : 1;
			var interval = Settings.Security.CbtKioskAttemptInterval;
			var settings = Context.CbtKioskClient.GetSettings(Settings.Security.CbtKioskUrl, Settings.Security.CbtKioskTimeout, attempts, interval);

			if (!settings.Success && !string.IsNullOrEmpty(Settings.Security.CbtFallbackUrl))
			{
				Logger.Warn($"Primary CBT kiosk endpoint unavailable ({settings.Message}), trying the configured fallback endpoint...");
				settings = Context.CbtKioskClient.GetSettings(Settings.Security.CbtFallbackUrl, Settings.Security.CbtKioskTimeout, attempts, interval);
			}

			if (!settings.Success)
			{
				Logger.Warn($"Failed to retrieve the quit password from the CBT kiosk endpoint(s): {settings.Message}.");

				if (!string.IsNullOrEmpty(Settings.Security.QuitPasswordHash))
				{
					Logger.Warn("Falling back to the locally configured quit password.");

					return IsValidLocalQuitPassword(password) ? QuitPasswordVerification.Valid : QuitPasswordVerification.Invalid;
				}

				Logger.Warn("No locally configured quit password available, denying access.");
				return QuitPasswordVerification.Unavailable;
			}

			if (settings.IsExpired || HasExpired(settings.PasswordExpiresAt))
			{
				Logger.Warn($"The quit password retrieved from the CBT kiosk endpoint has expired (expires at: {settings.PasswordExpiresAt}), denying access.");
				return QuitPasswordVerification.Expired;
			}

			if (string.IsNullOrEmpty(settings.ExitPassword))
			{
				Logger.Warn("The CBT kiosk endpoint did not provide a quit password, denying access.");
				return QuitPasswordVerification.Unavailable;
			}

			return string.Equals(settings.ExitPassword, password, StringComparison.Ordinal) ? QuitPasswordVerification.Valid : QuitPasswordVerification.Invalid;
		}

		/// <summary>
		/// Determines whether the given expiry timestamp lies in the past. This is a defensive check for the case where the CBT panel reports
		/// an expiry date but fails to flag the password as expired.
		/// </summary>
		private static bool HasExpired(DateTime? expiresAt)
		{
			return expiresAt.HasValue && expiresAt.Value < DateTime.Now;
		}

		protected void PrepareShutdown()
		{
			Context.Responsibilities.Delegate(ClientTask.PrepareShutdown_Wave1);
			Context.Responsibilities.Delegate(ClientTask.PrepareShutdown_Wave2);
		}

		protected void ResumeActivators()
		{
			foreach (var activator in Context.Activators)
			{
				activator.Resume();
			}
		}

		protected LockScreenResult ShowLockScreen(string message, string title, IEnumerable<LockScreenOption> options)
		{
			Logger.Info("Showing lock screen...");

			PauseActivators();
			Context.LockScreen = Context.UserInterfaceFactory.CreateLockScreen(message, title, options, Settings.UserInterface.LockScreen);
			Context.LockScreen.Show();

			if (Settings.SessionMode == SessionMode.Server)
			{
				SendLockScreenNotification(message);
			}

			var result = WaitForLockScreenResolution();

			Context.LockScreen.Close();
			Context.LockScreen = default;
			ResumeActivators();

			if (Settings.SessionMode == SessionMode.Server)
			{
				SendLockScreenConfirmation();
			}

			Logger.Info("Closed lock screen.");

			return result;
		}

		protected bool TryRequestShutdown()
		{
			PrepareShutdown();

			var communication = Context.Runtime.RequestShutdown();

			if (!communication.Success)
			{
				Logger.Error("Failed to communicate shutdown request to the runtime!");
				Context.MessageBox.Show(TextKey.MessageBox_QuitError, TextKey.MessageBox_QuitErrorTitle, icon: MessageBoxIcon.Error);
			}

			return communication.Success;
		}

		private void SendLockScreenConfirmation()
		{
			var response = Context.Server.ConfirmLockScreen();

			if (!response.Success)
			{
				Logger.Error($"Failed to send lock screen confirmation to server! Message: {response.Message}.");
			}
		}

		private void SendLockScreenNotification(string message)
		{
			var response = Context.Server.LockScreen(message);

			if (!response.Success)
			{
				Logger.Error($"Failed to send lock screen notification to server! Message: {response.Message}.");
			}
		}

		private LockScreenResult WaitForLockScreenResolution()
		{
			var hasQuitPassword = HasQuitPassword;
			var result = default(LockScreenResult);

			for (var unlocked = false; !unlocked;)
			{
				result = Context.LockScreen.WaitForResult();

				if (result.Canceled)
				{
					Logger.Info("The lock screen has been canceled automatically.");
					unlocked = true;
				}
				else if (hasQuitPassword)
				{
					var isCorrect = IsValidQuitPassword(result.Password);

					if (isCorrect)
					{
						Logger.Info("The user entered the correct unlock password.");
						unlocked = true;
					}
					else
					{
						Logger.Info("The user entered a wrong unlock password.");
						Context.MessageBox.Show(TextKey.MessageBox_InvalidUnlockPassword, TextKey.MessageBox_InvalidUnlockPasswordTitle, icon: MessageBoxIcon.Warning, parent: Context.LockScreen);
					}
				}
				else
				{
					Logger.Warn($"No unlock password is defined, allowing user to resume session!");
					unlocked = true;
				}
			}

			return result;
		}
	}
}

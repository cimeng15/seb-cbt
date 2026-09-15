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
			var valid = !string.IsNullOrEmpty(Settings.Security.CbtKioskUrl) && Context.CbtKioskClient != default
				? IsValidCbtKioskPassword(password)
				: IsValidLocalQuitPassword(password);

			if (valid)
			{
				Context.QuitPasswordValidated = true;
			}

			return valid;
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
		/// Validates the given password against the value currently configured on the CBT kiosk settings endpoint. If the endpoint cannot be
		/// reached, the locally configured quit password hash is used as a fallback (if any), so that an exam can always be terminated even
		/// when the CBT server is unavailable.
		/// </summary>
		private bool IsValidCbtKioskPassword(string password)
		{
			var settings = Context.CbtKioskClient.GetSettings(Settings.Security.CbtKioskUrl, Settings.Security.CbtKioskTimeout);

			if (!settings.Success)
			{
				Logger.Warn($"Failed to retrieve the quit password from the CBT kiosk endpoint: {settings.Message}.");
				Logger.Warn($"Falling back to the locally configured quit password{(string.IsNullOrEmpty(Settings.Security.QuitPasswordHash) ? " (none configured, access denied)" : string.Empty)}.");

				return !string.IsNullOrEmpty(Settings.Security.QuitPasswordHash) && IsValidLocalQuitPassword(password);
			}

			if (settings.IsExpired)
			{
				Logger.Warn("The quit password retrieved from the CBT kiosk endpoint is expired, denying access.");
				return false;
			}

			if (string.IsNullOrEmpty(settings.ExitPassword))
			{
				Logger.Warn("The CBT kiosk endpoint did not provide a quit password, denying access.");
				return false;
			}

			return string.Equals(settings.ExitPassword, password, StringComparison.Ordinal);
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

﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Plugin.Payments.Stripe.Models;
using Nop.Services;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Plugin.Payments.Stripe.Controllers
{
    public class PaymentStripeController : BasePaymentController
    {
        #region Fields

        private readonly ILocalizationService _localizationService;
#if NOP420
        private readonly INotificationService _notificationService;
#endif
        private readonly IPermissionService _permissionService;
        private readonly ISettingService _settingService;
        private readonly StripePaymentProcessor _stripePaymentManager;
        private readonly StripePaymentSettings _stripePaymentSettings;

#endregion

#region Ctor

        public PaymentStripeController(ILocalizationService localizationService,
#if NOP420
            INotificationService notificationService,
#endif
            IPermissionService permissionService,
            ISettingService settingService,
            StripePaymentSettings stripePaymentSettings)
        {
            this._localizationService = localizationService;
#if NOP420
            this._notificationService = notificationService;
#endif
            this._permissionService = permissionService;
            this._settingService = settingService;
            this._stripePaymentSettings = stripePaymentSettings;
        }

#endregion

#region Methods

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            //whether user has the authority
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //prepare model
            ConfigurationModel model = new ConfigurationModel
            {
                SecretKey = _stripePaymentSettings.SecretKey,
                PublishableKey = _stripePaymentSettings.PublishableKey,
                AdditionalFee = _stripePaymentSettings.AdditionalFee,
                AdditionalFeePercentage = _stripePaymentSettings.AdditionalFeePercentage
            };

            return View("~/Plugins/Payments.Stripe/Views/Configure.cshtml", model);
        }

        [HttpPost, ActionName("Configure")]
        [FormValueRequired("save")]
        [AuthorizeAdmin]
        [AutoValidateAntiforgeryToken]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            //whether user has the authority
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure().ConfigureAwait(false);

            //save settings
           
            _stripePaymentSettings.SecretKey = model.SecretKey;
            _stripePaymentSettings.PublishableKey = model.PublishableKey;
            _stripePaymentSettings.AdditionalFee = model.AdditionalFee;
            _stripePaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;
            _settingService.SaveSetting(_stripePaymentSettings);
#if NOP420
            _notificationService.SuccessNotification(_localizationService.GetResource("Admin.Plugins.Saved"));
#endif
            return await Configure();
        }

#endregion
    }
}
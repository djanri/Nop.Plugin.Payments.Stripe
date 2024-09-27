﻿using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Plugin.Payments.Stripe.Models;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Localization;

namespace Nop.Plugin.Payments.Stripe.Components
{
    [ViewComponent(Name = StripePaymentDefaults.ViewComponentName)]
    public class PaymentInfoViewComponent : ViewComponent
    {
        #region Fields

        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly IWorkContext _workContext;
        private readonly ICustomerService _customerService;
        private readonly IAddressService _addressService;
        //private readonly StripePaymentManager _stripePaymentManager;

        #endregion

        #region Ctor

        public PaymentInfoViewComponent(IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IWorkContext workContext,
            ICustomerService customerService,
            IAddressService addressService)
        {
            _genericAttributeService = genericAttributeService;
            _localizationService = localizationService;
            _workContext = workContext;
            _addressService = addressService;
            _customerService = customerService;
        }

        #endregion

        #region Methods

        public async Task<IViewComponentResult> InvokeAsync()
        {

            //!This is completely different.  Don't do it like this
            Customer customer = await _workContext.GetCurrentCustomerAsync();
            Address billingAddress = await _addressService.GetAddressByIdAsync((int)customer.BillingAddressId);
            Address shippingAddress = await _addressService.GetAddressByIdAsync((int)customer.ShippingAddressId);
            PaymentInfoModel model = new PaymentInfoModel
            {
                //whether current customer is guest

                IsGuest = await _customerService.IsGuestAsync(customer),
                
                PostalCode = billingAddress?.ZipPostalCode ?? shippingAddress?.ZipPostalCode
            };

            return View("~/Plugins/Payments.Stripe/Views/PaymentInfo.cshtml", model);
        }

        #endregion
    }
}
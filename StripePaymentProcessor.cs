﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Payments.Stripe.Components;
using Nop.Services.Attributes;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Services.Vendors;
using Stripe;

namespace Nop.Plugin.Payments.Stripe
{
    /// <summary>
    /// Support for the Stripe payment processor.
    /// </summary>
    public class StripePaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        public const string STRIPE_SECRET_KEY = "StripeSecretKey (starts with sk_)";
        public const string STRIPE_PUBLISHABLE_KEY = "StripePublishableKey (starts with pk_)";

        
        private readonly ICustomerService _customerService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly StripePaymentSettings _stripePaymentSettings;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IAddressService _addressService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly ICountryService _countryService;
        protected readonly IShoppingCartService _shoppingCartService;
        protected readonly IVendorService _vendorService;
        protected readonly IGenericAttributeService _genericAttributeService;
        protected readonly IAttributeParser<VendorAttribute, VendorAttributeValue> _vendorAttributeParser;
        protected readonly IAttributeService<VendorAttribute, VendorAttributeValue> _vendorAttributeService;

        #endregion

        #region Ctor

        public StripePaymentProcessor(
            ICustomerService customerService,
            ILocalizationService localizationService,
            ILogger logger,
            ISettingService settingService,
            IWebHelper webHelper,
            StripePaymentSettings stripePaymentSettings,
            IOrderTotalCalculationService orderTotalCalculationService,
            IAddressService addressService,
            IStateProvinceService stateProvinceService,
            ICountryService countryService,
            IShoppingCartService shoppingCartService,
            IVendorService vendorService,
            IGenericAttributeService genericAttributeService,
            IAttributeParser<VendorAttribute, VendorAttributeValue> vendorAttributeParser,
            IAttributeService<VendorAttribute,VendorAttributeValue> vendorAttributeService)
        {
            _customerService = customerService;
            _localizationService = localizationService;
            _logger = logger;
            _settingService = settingService;
            _webHelper = webHelper;
            _stripePaymentSettings = stripePaymentSettings;
            _orderTotalCalculationService = orderTotalCalculationService;
            _addressService = addressService;
            _stateProvinceService = stateProvinceService;
            _countryService = countryService;
            _shoppingCartService = shoppingCartService;
            _vendorService = vendorService;
            _genericAttributeService = genericAttributeService;
            _vendorAttributeParser = vendorAttributeParser;
            _vendorAttributeService = vendorAttributeService;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Convert a NopCommerce address to a Stripe API address
        /// </summary>
        /// <param name="nopAddress"></param>
        /// <returns></returns>
        private async Task<AddressOptions> MapNopAddressToStripeAsync(Core.Domain.Common.Address nopAddress)
        {
            return new AddressOptions
            {
                Line1 = nopAddress.Address1,
                City = nopAddress.City,
                State = (await _stateProvinceService.GetStateProvinceByIdAsync((int)nopAddress.StateProvinceId)).Abbreviation,
                PostalCode = nopAddress.ZipPostalCode,
                Country = (await _countryService.GetCountryByIdAsync((int)nopAddress.CountryId)).ThreeLetterIsoCode
            };
        }

        /// <summary>
        /// Set up for a call to the Stripe API
        /// </summary>
        /// <returns></returns>
        private RequestOptions GetStripeApiRequestOptions()
        {
            return new RequestOptions
            {
                ApiKey = _stripePaymentSettings.SecretKey,
                IdempotencyKey = Guid.NewGuid().ToString()
            };
        }


        /// <summary>
        /// Perform a shallow validation of a stripe token
        /// </summary>
        /// <param name="stripeTokenObj"></param>
        /// <returns></returns>
        private bool IsStripeTokenID(string token)
        {
            return token.StartsWith("tok_");
        }
        
        private bool IsChargeID(string chargeID)
        {
            return chargeID.StartsWith("ch_");
        }

        #endregion

        #region Methods

        public override string GetConfigurationPageUrl() => $"{_webHelper.GetStoreLocation()}Admin/PaymentStripe/Configure";

        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            throw new NotImplementedException("CancelRecurringPaymentAsync");
        }

        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            return Task.FromResult(false);
        }

        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");

            return Task.FromResult(result);
        }

        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            var result = await _orderTotalCalculationService.CalculatePaymentAdditionalFeeAsync(cart,
               _stripePaymentSettings.AdditionalFee, _stripePaymentSettings.AdditionalFeePercentage);

            return result;
        }

        public async Task< ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            var paymentRequest = new ProcessPaymentRequest();

            if (form.TryGetValue("stripeToken", out StringValues stripeToken) && !StringValues.IsNullOrEmpty(stripeToken))
                paymentRequest.CustomValues.Add(await _localizationService.GetResourceAsync("Plugins.Payments.Stripe.Fields.StripeToken.Key"), stripeToken.ToString());

            return paymentRequest;
        }

        public Type GetPublicViewComponent()
        {
            return typeof(PaymentInfoViewComponent);
        }

        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {

            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country

            return Task.FromResult(false);
        }

        public Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            throw new NotImplementedException("PostProcessPaymentRequest");
        }

        public async Task<string> GetVendorStripeValue(Core.Domain.Customers.Customer customer, int storeId, string stripeKey)
        {
            var cartItems = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, storeId);
            if (cartItems.Any())
            {
                var cartItem = cartItems.First();
                var vendor = await _vendorService.GetVendorByProductIdAsync(cartItem.ProductId);
                if (vendor != null)
                {
                    var vendorAttributesXml = await _genericAttributeService.GetAttributeAsync<string>(vendor, NopVendorDefaults.VendorAttributes);
                    if (!string.IsNullOrEmpty(vendorAttributesXml))
                    {
                        var vendorAttributes = await _vendorAttributeService.GetAllAttributesAsync();
                        var keyAttr = vendorAttributes.FirstOrDefault(attr => string.Equals(attr.Name, stripeKey));
                        if (keyAttr != null && keyAttr.AttributeControlType == AttributeControlType.TextBox)
                        {
                            var enteredText = _vendorAttributeParser.ParseValues(vendorAttributesXml, keyAttr.Id);
                            if (enteredText.Any())
                                return enteredText[0];
                        }
                    }
                }
            }
            return await Task.FromResult(string.Empty);
        }

        public async Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            //get customer
            var customer = await _customerService.GetCustomerByIdAsync(processPaymentRequest.CustomerId);
            if (customer == null)
                throw new NopException("Customer cannot be loaded");

            string tokenKey = await _localizationService.GetResourceAsync("Plugins.Payments.Stripe.Fields.StripeToken.Key");
            if (!processPaymentRequest.CustomValues.TryGetValue(tokenKey, out object stripeTokenObj) || !(stripeTokenObj is string) || !IsStripeTokenID((string)stripeTokenObj))
            {
                throw new NopException("Card token not received");
            }
            string stripeToken = stripeTokenObj.ToString();
            var service = new ChargeService();
            var chargeOptions = new ChargeCreateOptions
            {
                Amount = (long)(processPaymentRequest.OrderTotal * 100),
                Currency = "usd",
                Description = string.Format(StripePaymentDefaults.PaymentNote, processPaymentRequest.OrderGuid),
                Source = stripeToken,

            };
            var shippingAddress = await _addressService.GetAddressByIdAsync((int)customer.ShippingAddressId);
            if (shippingAddress != null)
            {
                chargeOptions.Shipping = new ChargeShippingOptions
                {
                    Address = await MapNopAddressToStripeAsync(shippingAddress),
                    Phone = shippingAddress.PhoneNumber,
                    Name = customer.FirstName + ' ' + customer.LastName
                };
            }

            var options = GetStripeApiRequestOptions();
            if (_stripePaymentSettings.IsIndividualByVendor)
            {
                var secretKey = await GetVendorStripeValue(customer, processPaymentRequest.StoreId, STRIPE_SECRET_KEY);
                if (!string.IsNullOrEmpty(secretKey))
                {
                    options.ApiKey = secretKey;
                }
            }
            var charge = service.Create(chargeOptions, options);

            var result = new ProcessPaymentResult();
            if (charge.Status == "succeeded")
            {
                result.NewPaymentStatus = PaymentStatus.Paid;
                result.AuthorizationTransactionId = charge.Id;
                result.AuthorizationTransactionResult = $"Transaction was processed by using {charge?.Source.Object}. Status is {charge.Status}";
                return result;
            }
            else
            {
                throw new NopException($"Charge error: {charge.FailureMessage}");
            }
        }

        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            throw new NotImplementedException("ProcessRecurringPaymentAsync");
        }

        /// <summary>
        /// Full or partial refund
        /// </summary>
        /// <param name="refundPaymentRequest"></param>
        /// <returns></returns>
        public async Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            string chargeID = refundPaymentRequest.Order.AuthorizationTransactionId;
            var orderAmtRemaining = refundPaymentRequest.Order.OrderTotal - refundPaymentRequest.AmountToRefund;
            bool isPartialRefund = orderAmtRemaining > 0;

            if (!IsChargeID(chargeID))
            {
                throw new NopException($"Refund error: {chargeID} is not a Stripe Charge ID. Refund cancelled");
            }
            var service = new RefundService();
            var refundOptions = new RefundCreateOptions
            {
                Charge = chargeID,
                Amount = (long)(refundPaymentRequest.AmountToRefund * 100),
                Reason = RefundReasons.RequestedByCustomer
            };
            var options = GetStripeApiRequestOptions();
            if (_stripePaymentSettings.IsIndividualByVendor)
            {
                var customer = await _customerService.GetCustomerByIdAsync(refundPaymentRequest.Order.CustomerId);
                if (customer != null)
                {
                    var secretKey = await GetVendorStripeValue(customer, refundPaymentRequest.Order.StoreId, STRIPE_SECRET_KEY);
                    if (!string.IsNullOrEmpty(secretKey))
                    {
                        options.ApiKey = secretKey;
                    }
                }
            }
            var refund = await service.CreateAsync(refundOptions, options);

            RefundPaymentResult result = new RefundPaymentResult();

            switch (refund.Status)
            {
                case "succeeded":
                    result.NewPaymentStatus = isPartialRefund ? PaymentStatus.PartiallyRefunded : PaymentStatus.Refunded;
                    break;

                case "pending":
                    result.NewPaymentStatus = PaymentStatus.Pending;
                    result.AddError($"Refund failed with status of ${refund.Status}");
                    break;

                default:
                    throw new NopException("Refund returned a status of ${refund.Status}");
            }
            return result;
        }

        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            IList<string> errors = new List<string>();
            if (!(form.TryGetValue("stripeToken", out StringValues stripeToken) || stripeToken.Count != 1 || !IsStripeTokenID(stripeToken[0])))
            {
                errors.Add("Token was not supplied or invalid");
            }
            return Task.FromResult(errors);
        }

        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        { throw new NotImplementedException("VoidAsync"); }

        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.Stripe.PaymentMethodDescription");
        }

        /// <summary>
        /// Install the plugin
        /// </summary>
        public override async Task InstallAsync()
        {
            //settings
            _settingService.SaveSetting(new StripePaymentSettings
            {
                AdditionalFee = 0,
                AdditionalFeePercentage = false
            });


            //locales            
            _localizationService.AddOrUpdateLocaleResource(new Dictionary<string, string>
            {
                ["Plugins.Payments.Stripe.Fields.SecretKey"] = "Secret key, live or test (starts with sk_)",
                ["Plugins.Payments.Stripe.Fields.PublishableKey"] = "Publishable key, live or test (starts with pk_)",
                ["Plugins.Payments.Stripe.Fields.AdditionalFee"] = "Additional fee",
                ["Plugins.Payments.Stripe.Fields.AdditionalFee.Hint"] = "Enter additional fee to charge your customers.",
                ["Plugins.Payments.Stripe.Fields.AdditionalFeePercentage"] = "Additional fee. Use percentage",
                ["Plugins.Payments.Stripe.Fields.AdditionalFeePercentage.Hint"] = "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.",
                ["Plugins.Payments.Stripe.Fields.IsIndividualByVendor"] = "Use keys from vendor settings instead of these",
                ["Plugins.Payments.Stripe.Fields.StripeToken.Key"] = "Stripe Token",
                ["Plugins.Payments.Stripe.PaymentMethodDescription"] = "Pay by credit / debit card",
                ["Plugins.Payments.Stripe.Instructions"] = @"
                <p>
                     For plugin configuration follow these steps:<br />
                    <br />
                    1. If you haven't already, create an account on Stripe.com and sign in<br />
                    2. In the Developers menu (left), choose the API Keys option.
                    3. You will see two keys listed, a Publishable key and a Secret key. You will need both. (If you'd like, you can create and use a set of restricted keys. That topic isn't covered here.)
                    <em>Stripe supports test keys and production keys. Use whichever pair is appropriate. There's no switch between test/sandbox and production other than using the appropriate keys.</em>
                    4. Paste these keys into the configuration page of this plug-in. (Both keys are required.) 
                    <br />
                    <em>Note: If using production keys, the payment form will only work on sites hosted with HTTPS. (Test keys can be used on http sites.) If using test keys, 
                    use these <a href='https://stripe.com/docs/testing'>test card numbers from Stripe</a>.</em><br />
                </p>"
            });

            
            var allVendorAttributes = await _vendorAttributeService.GetAllAttributesAsync();
            if (!allVendorAttributes.Any(attr => string.Equals(attr.Name, STRIPE_SECRET_KEY)))
            {
                var secretKeyAttr = new VendorAttribute
                {
                    Name = STRIPE_SECRET_KEY,
                    AttributeControlType = AttributeControlType.TextBox
                };
                await _vendorAttributeService.InsertAttributeAsync(secretKeyAttr);
            }
            if (!allVendorAttributes.Any(attr => string.Equals(attr.Name, STRIPE_PUBLISHABLE_KEY)))
            {
                var publishableKeyKeyAttr = new VendorAttribute
                {
                    Name = STRIPE_PUBLISHABLE_KEY,
                    AttributeControlType = AttributeControlType.TextBox
                };
                await _vendorAttributeService.InsertAttributeAsync(publishableKeyKeyAttr);
            }
            await base.InstallAsync();
        }


        /// <summary>
        /// Uninstall the plugin
        /// </summary>
        public override async Task UninstallAsync()
        {
            //settings
          await  _settingService.DeleteSettingAsync<StripePaymentSettings>();

            //locales
          await  _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Stripe");

          await base.UninstallAsync();
        }

        #endregion

        #region Properties

        public bool SupportCapture => false;

        public bool SupportPartiallyRefund => true;

        public bool SupportRefund => true;

        public bool SupportVoid => false;

        
        public RecurringPaymentType RecurringPaymentType
        {
            get { return RecurringPaymentType.NotSupported; }
        }

        public PaymentMethodType PaymentMethodType => PaymentMethodType.Standard;

        public bool SkipPaymentInfo => false;

        public string PaymentMethodDescription => "Stripe";

        #endregion
    }
}

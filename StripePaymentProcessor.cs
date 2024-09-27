using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Stripe.Components;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Stripe;

namespace Nop.Plugin.Payments.Stripe
{
    /// <summary>
    /// Support for the Stripe payment processor.
    /// </summary>
    public class StripePaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly CurrencySettings _currencySettings;
        private readonly ICurrencyService _currencyService;
        private readonly ICustomerService _customerService;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly ILogger _logger;
        private readonly IPaymentService _paymentService;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly StripePaymentSettings _stripePaymentSettings;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IAddressService _addressService;
        private readonly IStateProvinceService _stateProvinceService;
        private readonly ICountryService _countryService;

        #endregion

        #region Ctor

        public StripePaymentProcessor(CurrencySettings currencySettings,
            ICurrencyService currencyService,
            ICustomerService customerService,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            ILogger logger,
            IPaymentService paymentService,
            ISettingService settingService,
            IWebHelper webHelper,
            StripePaymentSettings stripePaymentSettings,
            IOrderTotalCalculationService orderTotalCalculationService,
            IAddressService addressService,
            IStateProvinceService stateProvinceService,
            ICountryService countryService)
        {
            _currencySettings = currencySettings;
            _currencyService = currencyService;
            _customerService = customerService;
            _genericAttributeService = genericAttributeService;
            _localizationService = localizationService;
            _logger = logger;
            _paymentService = paymentService;
            _settingService = settingService;
            _webHelper = webHelper;
            _stripePaymentSettings = stripePaymentSettings;
            _orderTotalCalculationService = orderTotalCalculationService;
            _addressService = addressService;
            _stateProvinceService = stateProvinceService;
            _countryService = countryService;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Convert a NopCommere address to a Stripe API address
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

            var charge = service.Create(chargeOptions, GetStripeApiRequestOptions());

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
        /// 
        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        { throw new NotImplementedException("RefundAsync"); }
        //public RefundPaymentResult Refund(RefundPaymentRequest refundPaymentRequest)
        //{
        //    string chargeID = refundPaymentRequest.Order.AuthorizationTransactionId;
        //    var orderAmtRemaining = refundPaymentRequest.Order.OrderTotal - refundPaymentRequest.AmountToRefund;
        //    bool isPartialRefund = orderAmtRemaining > 0;

        //    if (!IsChargeID(chargeID))
        //    {
        //        throw new NopException($"Refund error: {chargeID} is not a Stripe Charge ID. Refund cancelled");
        //    }
        //    var service = new RefundService();
        //    var refundOptions = new RefundCreateOptions
        //    {
        //        ChargeId = chargeID,
        //        Amount = (long)(refundPaymentRequest.AmountToRefund * 100),
        //        Reason = RefundReasons.RequestedByCustomer
        //    };
        //    var refund = service.Create(refundOptions, GetStripeApiRequestOptions());

        //    RefundPaymentResult result = new RefundPaymentResult();

        //    switch (refund.Status)
        //    {
        //        case "succeeded":
        //            result.NewPaymentStatus = isPartialRefund ? PaymentStatus.PartiallyRefunded : PaymentStatus.Refunded;
        //            break;

        //        case "pending":
        //            result.NewPaymentStatus = PaymentStatus.Pending;
        //            result.AddError($"Refund failed with status of ${ refund.Status }");
        //            break;

        //        default:
        //            throw new NopException("Refund returned a status of ${refund.Status}");
        //    }
        //    return result;
        //}

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
            return await _localizationService.GetResourceAsync("Plugins.Payments.BetterStripe.PaymentMethodDescription");
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
                ["Plugins.Payments.Stripe.Fields.StripeToken.Key"] = "Stripe Token",
                ["Plugins.Payments.Stripe.Fields.AdditionalFeePercentage.Hint"] = "Determines whether to apply a percentage additional fee to the order total. If not enabled, a fixed value is used.",
                ["Plugins.Payments.Stripe.Instructions"] = @"
                <p>
                     For plugin configuration follow these steps:<br />
                    <br />
                    1. If you haven't already, create an account on Stripe.com and sign in<br />
                    2. In the Developers menu (left), choose the API Keys option.
                    3. You will see two keys listed, a Publishable key and a Secret key. You will need both. (If you'd like, you can create and use a set of restricted keys. That topic isn't covered here.)
                    <em>Stripe supports test keys and production keys. Use whichever pair is appropraite. There's no switch between test/sandbox and proudction other than using the appropriate keys.</em>
                    4. Paste these keys into the configuration page of this plug-in. (Both keys are required.) 
                    <br />
                    <em>Note: If using production keys, the payment form will only work on sites hosted with HTTPS. (Test keys can be used on http sites.) If using test keys, 
                    use these <a href='https://stripe.com/docs/testing'>test card numbers from Stripe</a>.</em><br />
                </p>"
            });

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

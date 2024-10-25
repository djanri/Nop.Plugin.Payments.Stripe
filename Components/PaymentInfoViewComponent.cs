using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Vendors;
using Nop.Plugin.Payments.Stripe.Models;
using Nop.Services.Attributes;
using Nop.Services.Common;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Services.Vendors;

namespace Nop.Plugin.Payments.Stripe.Components
{
    [ViewComponent(Name = StripePaymentDefaults.ViewComponentName)]
    public class PaymentInfoViewComponent : ViewComponent
    {
        #region Fields

        private readonly IWorkContext _workContext;
        private readonly ICustomerService _customerService;
        private readonly IAddressService _addressService;
        protected readonly IStoreContext _storeContext;
        protected readonly IShoppingCartService _shoppingCartService;
        protected readonly IVendorService _vendorService;
        protected readonly IGenericAttributeService _genericAttributeService;
        protected readonly IAttributeParser<VendorAttribute, VendorAttributeValue> _vendorAttributeParser;
        protected readonly IAttributeService<VendorAttribute, VendorAttributeValue> _vendorAttributeService;
        private readonly StripePaymentSettings _stripePaymentSettings;

        #endregion

        #region Ctor

        public PaymentInfoViewComponent(
            IWorkContext workContext,
            ICustomerService customerService,
            IAddressService addressService,
            IStoreContext storeContext,
            IShoppingCartService shoppingCartService,
            IVendorService vendorService,
            IGenericAttributeService genericAttributeService,
            IAttributeParser<VendorAttribute, VendorAttributeValue> vendorAttributeParser,
            IAttributeService<VendorAttribute, VendorAttributeValue> vendorAttributeService,
            StripePaymentSettings stripePaymentSettings)
        {
            _workContext = workContext;
            _addressService = addressService;
            _customerService = customerService;
            _storeContext = storeContext;
            _shoppingCartService = shoppingCartService;
            _vendorService = vendorService;
            _genericAttributeService = genericAttributeService;
            _vendorAttributeParser = vendorAttributeParser;
            _vendorAttributeService = vendorAttributeService;
            _stripePaymentSettings = stripePaymentSettings;
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
            if (_stripePaymentSettings.IsIndividualByVendor)
            {
                var store = await _storeContext.GetCurrentStoreAsync();
                var cartItems = await _shoppingCartService.GetShoppingCartAsync(customer, ShoppingCartType.ShoppingCart, store.Id);
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
                            var publishableKeyAttr = vendorAttributes.FirstOrDefault(attr => string.Equals(attr.Name, StripePaymentProcessor.STRIPE_PUBLISHABLE_KEY));
                            if (publishableKeyAttr != null && publishableKeyAttr.AttributeControlType == AttributeControlType.TextBox)
                            {                            
                                var enteredText = _vendorAttributeParser.ParseValues(vendorAttributesXml, publishableKeyAttr.Id);
                                if (enteredText.Any())
                                    model.VendorPublishableKey = enteredText[0];
                            }
                        }
                    }
                }
            }
            return View("~/Plugins/Payments.Stripe/Views/PaymentInfo.cshtml", model);
        }

        #endregion
    }
}
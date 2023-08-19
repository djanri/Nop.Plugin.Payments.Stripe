using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Rendering;
//using Nop.Plugin.Payments.Stripe.Validators;
using Nop.Web.Framework.Mvc.ModelBinding;
using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.Stripe.Models
{
    //[Validator(typeof(ConfigurationModelValidator))]
    public record ConfigurationModel : BaseNopModel
    {
        #region Ctor

        public ConfigurationModel()
        {
           
        }

        #endregion

        #region Properties

        [NopResourceDisplayName("Plugins.Payments.Stripe.Fields.SecretKey")]
        public string SecretKey { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Stripe.Fields.PublishableKey")]
        public string PublishableKey { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Stripe.Fields.AdditionalFee")]
        public decimal AdditionalFee { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Stripe.Fields.AdditionalFeePercentage")]
        public bool AdditionalFeePercentage { get; set; }

        #endregion
    }
}
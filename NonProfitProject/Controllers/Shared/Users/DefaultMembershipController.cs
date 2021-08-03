﻿using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NonProfitProject.Areas.Admin.Models.ViewModels;
using NonProfitProject.Code;
using NonProfitProject.Code.Security;
using NonProfitProject.Models;
using NonProfitProject.Models.Extensions;
using NonProfitProject.Models.ViewModels;
using NonProfitProject.Models.ViewModels.Shared.Users;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NonProfitProject.Controllers.Shared.Users
{
    public class DefaultMembershipController : Controller
    {
        protected NonProfitContext context;
        protected UserManager<User> userManager;

        public async Task<IActionResult> Index() 
        {
            var currentUser = await userManager.GetUserAsync(User);
            if(!await userManager.IsInRoleAsync(currentUser, "Member"))
            {
                return RedirectToAction("SignUp");
            }
            var receipts = context.Receipts.Include(r => r.MembershipDue).ThenInclude(md => md.MembershipType).Include(r => r.InvoicePayment).Include(r => r.User).Where(r => r.Donation == null && r.UserID == User.FindFirstValue(ClaimTypes.NameIdentifier)).OrderByDescending(r => r.Date).ToList();
            var userMembership = context.MembershipDues.Include(md => md.MembershipType).Where(md => md.UserID == currentUser.Id).OrderBy(md => md.MemDuesID).ToList();          
            if(receipts.Count() != 0)
            {
                var memberSince = MembershipDues.GetConsecutiveDate(userMembership);
                TimeSpan timespan = (TimeSpan)(DateTime.UtcNow - memberSince);
                ViewBag.TimeSpan = new List<int>
                {
                    //years
                    (int)Math.Floor(timespan.TotalDays / 365),
                    //motnhs
                    (int)Math.Floor((timespan.TotalDays / 30) % 12),
                    //days
                    (int)(timespan.Days) % 30,
                    //timespan.Days,
                    timespan.Hours,
                    timespan.Minutes,
                    timespan.Seconds
                };
                return View(receipts);
            }           
            return View("LifetimeMembership");
            
        }

        public IActionResult Details(int id)
        {
            var membership = context.Receipts.Include(r => r.MembershipDue).ThenInclude(md => md.MembershipType).Include(r => r.InvoicePayment).Include(r => r.User).Where(r => r.Donation == null && r.ReceiptID == id).OrderBy(r => r.Date).FirstOrDefault();
            if (membership == null)
            {
                return RedirectToAction("Index");
            }
            EditMembershipDueViewModel model = new EditMembershipDueViewModel()
            {
                ReceiptID = membership.ReceiptID,
                Username = membership.User.UserName,
                Total = membership.MembershipDue.MembershipType.Amount,
                FirstName = membership.User.UserFirstName,
                LastName = membership.User.UserLastName,
                Email = membership.User.Email,
                Phone = membership.User.PhoneNumber,
                MembershipType = membership.MembershipDue.MembershipType.Name,
                StartDate = membership.MembershipDue.MemStartDate,
                EndDate = membership.MembershipDue.MemEndDate,
                RenewalDate = membership.MembershipDue.MemRenewalDate,
                CancelDate = membership.MembershipDue.MemCancelDate,
                Active = membership.MembershipDue.MemActive,
                CardholderName = membership.InvoicePayment.CardholderName,
                CardNumber = membership.InvoicePayment.Last4Digits.ToString(),
                CardType = membership.InvoicePayment.CardType,
                ExpDate = membership.InvoicePayment.ExpDate,
                BillingFirstName = membership.InvoicePayment.BillingFirstName,
                BillingLastName = membership.InvoicePayment.BillingLastName,
                BillingAddr1 = membership.InvoicePayment.BillingAddr1,
                BillingAddr2 = membership.InvoicePayment.BillingAddr2,
                BillingCity = membership.InvoicePayment.BillingCity,
                BillingState = membership.InvoicePayment.BillingState,
                BillingPostalCode = membership.InvoicePayment.BillingPostalCode

            };
            return View(model);
        }

        [HttpGet]
        public IActionResult SignUp()
        {           
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Signup(string name)
        {
            if (User.IsInRole("Member"))
            {
                return RedirectToAction("Index");
            }
            var memType = context.MembershipTypes.Where(mt => mt.Name == name).FirstOrDefault();
            if(memType == null)
            {
                return View();
            }
            var user = await userManager.GetUserAsync(User);
            var membershipViewModel = new SignupMembershipViewModel {
                Membership = new MembershipDues()
                {
                    MembershipType = memType,
                    User = user
                }
            }; 

            var sessionModel = HttpContext.Session.GetObject<SignupMembershipViewModel>("SignupMembershipModel");

            if (sessionModel == null || sessionModel.PaymentViewModel == null)
            {
                HttpContext.Session.SetObject("SignupMembershipModel", membershipViewModel);
                return RedirectToAction("Payment");
            }
            else
            {
                membershipViewModel.PaymentViewModel = sessionModel.PaymentViewModel;
                HttpContext.Session.SetObject("SignupMembershipModel", membershipViewModel);
                return RedirectToAction("CheckOut");
            }
                      
        }

        [HttpGet]
        public IActionResult Payment()
        {
            if (User.IsInRole("Member"))
            {
                return RedirectToAction("Index");
            }
            if (HttpContext.Session.GetObject<SignupMembershipViewModel>("SignupMembershipModel") == null)
            {
                return RedirectToAction("Index");
            }
            return View();
        }


        [HttpPost]
        public IActionResult Payment(DonationPaymentViewModel model)
        {
            if (User.IsInRole("Member"))
            {
                return RedirectToAction("Index");
            }
            var sessionModel = HttpContext.Session.GetObject<SignupMembershipViewModel>("SignupMembershipModel");
            if (sessionModel == null)
            {
                TempData["SessionTimeout"] = "Session has timed out";
                return RedirectToAction("Index");
            }
            if (DateTime.ParseExact(model.ExpDate, "MM/yy", CultureInfo.InvariantCulture) < DateTime.UtcNow)
            {
                ModelState.AddModelError("ExpDate", "This payment method has expired");
            }
            if (model.CardNumber.Length != 19)
            {
                ModelState.AddModelError("CardNumber", "This payment is incomplete");
            }
            if (model.CVV.Length != 3)
            {
                ModelState.AddModelError("CVV", "CVV requires 3 digits");
            }
            if (ModelState.IsValid)
            {
                sessionModel.PaymentViewModel = model;
                HttpContext.Session.SetObject("SignupMembershipModel", sessionModel);
                return RedirectToAction("CheckOut");
            }
            return View();
        }

        public IActionResult CheckOut()
        {
            if (User.IsInRole("Member"))
            {
                return RedirectToAction("Index");
            }
            var sessionModel = HttpContext.Session.GetObject<SignupMembershipViewModel>("SignupMembershipModel");
            if (sessionModel == null)
            {
                TempData["SessionTimeout"] = "Session has timed out";
                return RedirectToAction("SignUp");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> PlaceOrder()
        {
            if (User.IsInRole("Member"))
            {
                return RedirectToAction("Index");
            }
            var sessionModel = HttpContext.Session.GetObject<SignupMembershipViewModel>("SignupMembershipModel");
            if (sessionModel == null)
            {
                TempData["SessionTimeout"] = "Session has timed out";
                return RedirectToAction("SignUp");
            }
            Receipts receipt = new Receipts()
            {
                UserID = User.FindFirstValue(ClaimTypes.NameIdentifier),
                Total = (decimal)sessionModel.Membership.MembershipType.Amount,
                Date = DateTime.UtcNow,
                Description = "Membership Due"
            };
            receipt.MembershipDue = new MembershipDues()
            {
                UserID = User.FindFirstValue(ClaimTypes.NameIdentifier),
                MembershipTypeID = sessionModel.Membership.MembershipType.MembershipTypeID,
                MemStartDate = DateTime.UtcNow,
                MemEndDate = DateTime.UtcNow.AddMonths(1),
                MemRenewalDate = DateTime.UtcNow.AddMonths(1),
                MemActive = true,
            };
            AesEncryption aes = new AesEncryption();
            receipt.InvoicePayment = new InvoicePayment()
            {
                CardholderName = sessionModel.PaymentViewModel.CardholderName,
                CardType = sessionModel.PaymentViewModel.CardType,
                CardNumber = aes.Encrypt(sessionModel.PaymentViewModel.CardNumber),
                ExpDate = sessionModel.PaymentViewModel.ExpDate,
                CVV = aes.Encrypt(sessionModel.PaymentViewModel.CVV),
                Last4Digits = Int32.Parse(sessionModel.PaymentViewModel.CardNumber[^4..]),
                BillingFirstName = sessionModel.PaymentViewModel.BillingFirstName,
                BillingLastName = sessionModel.PaymentViewModel.BillingLastName,
                BillingAddr1 = sessionModel.PaymentViewModel.BillingAddr1,
                BillingAddr2 = sessionModel.PaymentViewModel.BillingAddr2,
                BillingCity = sessionModel.PaymentViewModel.BillingCity,
                BillingState = sessionModel.PaymentViewModel.BillingState,
                BillingPostalCode = sessionModel.PaymentViewModel.BillingPostalCode
            };
            context.Receipts.Add(receipt);
            //HttpContext.Session.SetObject("ReviewOrder", receipt);
            //checks to see if the user wants to save payments. If they do, it checks to see if the card has already been saved. if it has, it doesn't save the payment
            if (sessionModel.PaymentViewModel.savePayment == true && context.SavedPayments.Where(sp => sp.CardNumber.Equals(aes.Encrypt(sessionModel.PaymentViewModel.CardNumber)) && sp.UserID == User.FindFirstValue(ClaimTypes.NameIdentifier)).AsNoTracking().FirstOrDefault() == null)
            {
                context.SavedPayments.Add(new SavedPaymentInformation()
                {
                    UserID = User.FindFirstValue(ClaimTypes.NameIdentifier),
                    CardholderName = sessionModel.PaymentViewModel.CardholderName,
                    CardType = sessionModel.PaymentViewModel.CardType,
                    CardNumber = aes.Encrypt(sessionModel.PaymentViewModel.CardNumber),
                    ExpDate = sessionModel.PaymentViewModel.ExpDate,
                    CVV = aes.Encrypt(sessionModel.PaymentViewModel.CVV),
                    Last4Digits = Int32.Parse(sessionModel.PaymentViewModel.CardNumber[^4..]),
                    BillingFirstName = sessionModel.PaymentViewModel.BillingFirstName,
                    BillingLastName = sessionModel.PaymentViewModel.BillingLastName,
                    BillingAddr1 = sessionModel.PaymentViewModel.BillingAddr1,
                    BillingAddr2 = sessionModel.PaymentViewModel.BillingAddr2,
                    BillingCity = sessionModel.PaymentViewModel.BillingCity,
                    BillingState = sessionModel.PaymentViewModel.BillingState,
                    BillingPostalCode = sessionModel.PaymentViewModel.BillingPostalCode
                });
            }
            context.SaveChanges();
            var user = await userManager.GetUserAsync(User);
            await userManager.AddToRoleAsync(user, "Member");
            await userManager.RemoveFromRoleAsync(user, "User");
            EmailManager email = new EmailManager(context);
            var message = email.CreateSimpleMessage(String.Format("{0} Membership for Non-Paw-Fit Animal Rescue",sessionModel.Membership.MembershipType), String.Format("Thank you, {0}, for becoming a {1} member of the Non-Paw-Fit Animal Rescue Foundation! You're continual efforts to create a better life for all animals will not go unnoticed. \n\n    Receipt Information: \n        Receipt ID: {2} \n        Total: {3:C} \n        Date: {4} \n\n    Membership Information: \n        Membership ID: {5} \n        Membership purchase: {1} \n        Payment per Month: {6:C} \n        Membership for User: {7} \n        Start Date: {8} \n        End Date: {9} \n        Renewal Date: {10} \n\n    Card Information: \n        CardHolder Name: {11} \n        Card Type: {12} \n        Card Number: xxxx-xxxx-xxxx-{13} \n        Expiration Date: {14} \n    Billing Information \n        Name: {15} \n        Address 1: {16} \n        Address 2: {17} \n        City: {18} \n        State: {19} \n        Postal Code: {20}", receipt.User.UserFirstName + " " + receipt.User.UserLastName, sessionModel.Membership.MembershipType.Name , receipt.ReceiptID, receipt.Total, receipt.Date, receipt.MembershipDue.MembershipTypeID, sessionModel.Membership.MembershipType.Amount, receipt.User.UserName, receipt.MembershipDue.MemStartDate, receipt.MembershipDue.MemEndDate, receipt.MembershipDue.MemRenewalDate, receipt.InvoicePayment.CardholderName, receipt.InvoicePayment.CardType, receipt.InvoicePayment.Last4Digits, receipt.InvoicePayment.ExpDate, receipt.InvoicePayment.BillingFirstName + " " + receipt.InvoicePayment.BillingLastName, receipt.InvoicePayment.BillingAddr1, receipt.InvoicePayment.BillingAddr2, receipt.InvoicePayment.BillingCity, receipt.InvoicePayment.BillingState, receipt.InvoicePayment.BillingPostalCode));

            email.SendEmail(new User() { UserFirstName = receipt.User.UserFirstName, Email = receipt.User.Email }, message);
            return RedirectToAction("Index");
        }
        [HttpPost]
        public IActionResult CancelOrder()
        {
            HttpContext.Session.Remove("SignupMembershipModel");
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> CancelMembership()
        {
            var membership = context.MembershipDues.Where(md => md.UserID == User.FindFirstValue(ClaimTypes.NameIdentifier) && md.MemActive == true).OrderBy(md => md.MemDuesID).LastOrDefault();
            if(membership == null)
            {
                TempData["MembershipManagment"] = String.Format("A membership asssociated with ID {0} is invalid", membership.MemDuesID);
                return RedirectToAction("Index");
            }
            membership.MemCancelDate = DateTime.UtcNow;
            membership.MemActive = false;
            var user = await userManager.GetUserAsync(User);
            await userManager.RemoveFromRoleAsync(user, "Member");
            if (!await userManager.IsInRoleAsync(user, "Admin") && !await userManager.IsInRoleAsync(user, "Employee"))
            {
                await userManager.AddToRoleAsync(user, "User");
            }
            return RedirectToAction("Signup");
        }
    }
}

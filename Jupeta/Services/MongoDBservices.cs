﻿using Jupeta.Models.DBModels;
using Jupeta.Models.RequestModels;
using Jupeta.Models.ResponseModels;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Newtonsoft.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;


namespace Jupeta.Services
{
    public class MongoDBservices : IMongoDBservices
    {
        private readonly IMongoCollection<UserReg> _users;
        private readonly IMongoCollection<Products> _products;
        private readonly IMongoCollection<Carts> _carts;
        private readonly IMongoCollection<Categories> _categories;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IFileService _fileService;
        private readonly HttpClient _httpClient;

        public MongoDBservices(IMongoDBSettings mongoSettings, IConfiguration config, IMongoClient mongoClient,
            IHttpContextAccessor httpContextAccessor, IFileService fileService, HttpClient httpClient)
        {
            //MongoClient client = new MongoClient(mongoSettings.ConnectionURI);
            var database = mongoClient.GetDatabase(mongoSettings.DatabaseName);
            _users = database.GetCollection<UserReg>("users");
            _products = database.GetCollection<Products>("products");
            _carts = database.GetCollection<Carts>("carts");
            _categories = database.GetCollection<Categories>("categories");
            _config = config;
            _httpContextAccessor = httpContextAccessor;
            _fileService = fileService;
            _httpClient = httpClient;
        }


        //Hash User password
        public static string CreatePasswordhash(string password)
        {
            if (password != null)
            {
                password = BCrypt.Net.BCrypt.HashPassword(password);

                return password;
            }
            else throw new Exception("invalid");

        }

        private async Task<bool> IsPhoneValid(long? phoneNumber)
        {
            if (!phoneNumber.HasValue)
            {
                return false;
            }
            var accesskey = _config.GetValue<string>("accesskey");
            var apiUrl = $"http://apilayer.net/api/validate?access_key={accesskey}&number={phoneNumber}";

            try
            {
                var response = await _httpClient.GetAsync(apiUrl);
                response.EnsureSuccessStatusCode();

                var responseData = await response.Content.ReadAsStringAsync();
                var validationResponse = JsonConvert.DeserializeObject<PhoneNumberValidationResponse>(responseData)!;
                if (validationResponse.valid)
                {
                    return true; // Set the return value to true if the "valid" property is true
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }

            return false;
        }

        private class PhoneNumberValidationResponse
        {
            public bool valid { get; set; }
            public string? number { get; set; }
            public string? local_format { get; set; }
            public string? international_format { get; set; }
            public string? country_prefix { get; set; }
            public string? country_code { get; set; }
            public string? country_name { get; set; }
            public string? location { get; set; }
            public string? carrier { get; set; }
            public string? line_type { get; set; }
        }

        //get all Users
        public List<UserReg> GetUsers() => _users.Find(user => true).SortByDescending(user => user.CreatedOn).ToList();

        //Get User by email
        public UserReg GetUser(string email) => _users.Find<UserReg>(user => user.Email == email).FirstOrDefault();

        //Add new User
        public async Task AddUser(AddUserModel user)
        {
            //check if email exists
            var IsEmail = await _users.Find(p => p.Email == user.Email).FirstOrDefaultAsync();
            if (IsEmail is not null)
            {
                throw new Exception("User Already Exists");
            }
            var passwordHash = CreatePasswordhash(user.Password);

            // Validate phone number
            if (!await IsPhoneValid(user.PhoneNumber))
            {
                throw new Exception("Invalid Phone Number");
            }

            UserReg dbTable = new()
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                Email = user.Email,
                PasswordHash = passwordHash,
                PhoneNumber = user.PhoneNumber,
                DateOfBirth = user.DateOfBirth,
                CreatedOn = DateTime.UtcNow
            };
            await _users.InsertOneAsync(dbTable);

        }


        //Edit user profile
        public async Task EditUser(EditUserModel user)
        {
            //check if email exists and set inputted fields
            try
            {
                var filter = Builders<UserReg>.Filter.Eq(u => u.Email, user.Email);
                var update = Builders<UserReg>.Update.Set(u => u.ModifiedOn, DateTime.UtcNow);

                if (!string.IsNullOrEmpty(user.FirstName))
                {
                    update = update.Set(u => u.FirstName, user.FirstName);
                }

                if (!string.IsNullOrEmpty(user.LastName))
                {
                    update = update.Set(u => u.LastName, user.LastName);
                }

                if (user.PhoneNumber.HasValue)
                {
                    // Validate phone number
                    if (!await IsPhoneValid(user.PhoneNumber))
                    {
                        throw new Exception("Invalid Phone Number");
                    }
                    update = update.Set(u => u.PhoneNumber, user.PhoneNumber.Value);
                }

                if (user.DateOfBirth.HasValue)
                {
                    update = update.Set(u => u.DateOfBirth, user.DateOfBirth);
                }

                await _users.UpdateOneAsync(filter, update);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }

        }


        //Login 
        public async Task<object> Login(UserLogin user)
        {
            var dbUser = await _users.Find(x => x.Email == user.Email).FirstOrDefaultAsync();

            if (dbUser == null)
            {
                throw new Exception("Email or Password is Incorrect");
            }

            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(user.PasswordHash, dbUser.PasswordHash);
            if (!isPasswordValid)
            {
                throw new Exception("Email or Password is Incorrect");
            }

            string token = CreateToken(user);
            return new TokenResponse
            {
                Id = dbUser.Id,
                Email = user.Email,
                Token = token
            };
        }


        //Create Token for authentication
        public string CreateToken(UserLogin user)
        {
            List<Claim> claims = new List<Claim>()
            {
                new Claim(ClaimTypes.Email, user.Email)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
                _config.GetSection("JwtConfig:Secret").Value!));

            var cred = new SigningCredentials(key, SecurityAlgorithms.HmacSha512);

            var token = new JwtSecurityToken(
                 _config["JwtConfig:Issuer"],
                _config["JwtConfig:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddHours(5),
                signingCredentials: cred
                );

            var jwt = new JwtSecurityTokenHandler().WriteToken(token);
            return jwt;
        }

        //add new products
        public async Task AddProduct(AddProductModel product)
        {
            if (product.ImageFile != null)
            {
                Guid id = Guid.NewGuid();
                var fileResult = await _fileService.UploadImage(id, product.ImageFile);
                if (fileResult is not null)
                {
                    string imageId = id.ToString();
                    Products dbTable = new()
                    {
                        ProductName = product.ProductName,
                        Description = product.Description,
                        Summary = product.Summary,
                        Price = product.Price,
                        IsAvailable = product.IsAvailable,
                        Quantity = product.Quantity,
                        ProductImage = id, // getting name of image
                        ImageFileUrl = "https://jupetaprojects3.s3.amazonaws.com/product_images/" + imageId + ".png",
                        AddedAt = DateTime.UtcNow
                    };
                    await _products.InsertOneAsync(dbTable);
                }
                else
                {
                    throw new Exception("Error Uploading Image...");
                }
            }
        }

        //get product by id
        public async Task<Products> GetProductById(string id)
        {
            return await Task.Run(() => _products.Find(product => product.Id == id).FirstOrDefault());
        }



        //get all products
        public async Task<PagedList<Products>> GetAllProducts(PageParameters param)
        {
            var query = await _products.Find(p => true).SortByDescending(p => p.AddedAt).ToListAsync();

            var pagedList = PagedList<Products>.ToPagedList(query.AsQueryable(), param.PageNumber, param.PageSize);

            return await pagedList;
        }


        //get available products
        public async Task<PagedList<Products>> GetAvailableProducts(PageParameters param)
        {
            var query = await _products.Find(p => p.IsAvailable == true).SortByDescending(p => p.AddedAt).ToListAsync();

            var pagedList = PagedList<Products>.ToPagedList(query.AsQueryable(), param.PageNumber, param.PageSize);

            return await pagedList;
        }

        //create a category
        public async Task CreateCategory(Categories model)
        {
            var categoryExists = await _categories.Find(c => c.Name.ToLower() == model.Name.ToLower()).FirstOrDefaultAsync();
            if (categoryExists != null)
            {
                throw new Exception("Category already exists.");
            }
            await _categories.InsertOneAsync(model);
        }


        //add to cart
        public async Task AddToCart(string id, string userId)
        {
            var product = await _products.Find(p => p.Id == id).FirstOrDefaultAsync();
            var existingCart = await _carts.Find(c => c.UserId == userId).FirstOrDefaultAsync();

            if (product.IsAvailable == false)
            {
                throw new Exception("Product is out of stock");
            }
            if (existingCart is null)
            {
                // Create a new cart if the user doesn't have one
                Carts newCart = new()
                {
                    UserId = userId,
                    DateAdded = DateTime.UtcNow,
                    Products = new List<Products>
                    {
                        new Products
                        {
                            Id = product.Id,
                            ProductName = product.ProductName,
                            ProductImage = product.ProductImage,
                            Price = product.Price,
                            Quantity = product.Quantity,
                            ImageFileUrl = product.ImageFileUrl,
                            AddedAt = product.AddedAt
                        }
                    }
                };

                await _carts.InsertOneAsync(newCart);
            }
            else
            {
                // Add the product to the existing cart's product list
                existingCart.Products!.Add(new Products
                {
                    Id = product.Id,
                    ProductName = product.ProductName,
                    ProductImage = product.ProductImage,
                    Price = product.Price,
                    Quantity = product.Quantity,
                    ImageFileUrl = product.ImageFileUrl,
                    AddedAt = product.AddedAt
                });
                //Update cart with new product entry
                var update = Builders<Carts>.Update.Set(c => c.Products, existingCart.Products);
                await _carts.UpdateOneAsync(c => c.UserId == userId, update);
            }

        }

        //view cart
        public async Task<(Carts carts, double totalPrice)> ViewCart(string id)
        {
            var carts = await _carts.Find(c => c.UserId == id).FirstOrDefaultAsync();
            double totalPrice = 0;
            if (carts is null)
            {
                throw new Exception("Your Cart is empty");
            }
            else
            {
                foreach (var product in carts.Products!)
                {
                    totalPrice += product.Price;
                }
            }

            return (carts, totalPrice);
        }


        //delete item from cart
        public async Task DeleteItem(string id, string userId)
        {
            var carts = await _carts.Find(c => c.UserId == id).FirstOrDefaultAsync();
            if (carts is null)
            {
                throw new Exception("Your Cart is empty");
            }
            else
            {
                var filter = Builders<Carts>.Filter.And(
                    Builders<Carts>.Filter.Eq(c => c.UserId, userId),
                    Builders<Carts>.Filter.ElemMatch(c => c.Products, p => p.Id == id)
                );

                var update = Builders<Carts>.Update.PullFilter(c => c.Products, p => p.Id == id);

                await _carts.UpdateOneAsync(filter, update);
            }

        }

        //sort products
        public async Task<PagedList<Products>> SearchSortBy(string? sortBy, string? keyword, bool isDescending, PageParameters param)
        {
            try
            {
                //isDescending = true;
                List<Products> products = new List<Products>();
                //search
                if (!string.IsNullOrEmpty(keyword))
                {
                    // Apply search filter
                    products = await _products.Find(p => p.ProductName.ToLower().Contains(keyword.ToLower()) ||
                                               p.Description.ToLower().Contains(keyword.ToLower())).ToListAsync();
                }

                //sorting                 
                switch (sortBy)
                {
                    case "name":
                        products = isDescending ? products.OrderByDescending(p => p.ProductName).ToList() : products.OrderBy(p => p.ProductName).ToList();
                        break;
                    case "price":
                        products = isDescending ? products.OrderByDescending(p => p.Price).ToList() : products.OrderBy(p => p.Price).ToList();
                        break;
                    case "date":
                        products = isDescending ? products.OrderByDescending(p => p.AddedAt).ToList() : products.OrderBy(p => p.AddedAt).ToList();
                        break;
                    default:
                        products = products.OrderByDescending(p => p.AddedAt).ToList();
                        break;
                }

                var pagedList = PagedList<Products>.ToPagedList(products.AsQueryable(), param.PageNumber, param.PageSize);

                return await pagedList;

            }
            catch (Exception ex) { throw new Exception(ex.Message); }
        }
    }
}
//another way to do SWITCH expression
//     SortDefinition<Products> sortDefinition = sortBy switch
//                {
//                    "name" => isDescending ? Builders<Products>.Sort.Descending(p => p.ProductName) : Builders<Products>.Sort.Ascending(p => p.ProductName),
//                    "price" => isDescending? Builders<Products>.Sort.Descending(p => p.Price) : Builders<Products>.Sort.Ascending(p => p.Price),
//                    "date" => isDescending? Builders<Products>.Sort.Descending(p => p.AddedAt) : Builders<Products>.Sort.Ascending(p => p.AddedAt),
//                    _ => throw new ArgumentException("Invalid sort parameter."),
//                };

//cascade delete and update on product changes in cart and product list
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services
{
    public interface IRecipeSeederService
    {
        Task<RecipeSeederResult> SeedRecipesAsync(int count = 100, bool dryRun = false, bool updateImages = false);
    }

    public class RecipeSeederResult
    {
        public int RecipesCreated { get; set; }
        public int RecipesSkipped { get; set; }
        public int ImagesDownloaded { get; set; }
        public int IngredientsCreated { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> SkippedReasons { get; set; } = new();
        public List<string> CreatedRecipes { get; set; } = new();
    }

    public class RecipeSeederService : IRecipeSeederService
    {
        private readonly PlaterlyDbContext _db;
        private readonly IBlobService _blobService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<RecipeSeederService> _logger;

        public RecipeSeederService(
            PlaterlyDbContext db,
            IBlobService blobService,
            IHttpClientFactory httpClientFactory,
            IConfiguration config,
            ILogger<RecipeSeederService> logger)
        {
            _db = db;
            _blobService = blobService;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        public async Task<RecipeSeederResult> SeedRecipesAsync(int count = 100, bool dryRun = false, bool updateImages = false)
        {
            var result = new RecipeSeederResult();
            var pexelsApiKey = _config["Pexels:ApiKey"];

            if (string.IsNullOrEmpty(pexelsApiKey))
            {
                result.Errors.Add("Pexels API key not configured. Add Pexels:ApiKey to appsettings.");
                return result;
            }

            // Get or create system user
            var systemUser = await GetOrCreateSystemUserAsync(dryRun);
            if (systemUser == null)
            {
                result.Errors.Add("Failed to get/create system user");
                return result;
            }

            var httpClient = _httpClientFactory.CreateClient();
            var recipes = GetCuratedHalalRecipes();
            var processedCount = 0;

            foreach (var template in recipes)
            {
                if (processedCount >= count) break;

                try
                {
                    // Check if recipe already exists
                    var existingRecipe = await _db.Recipes
                        .FirstOrDefaultAsync(r => r.Title == template.Title && r.UserId == systemUser.Id);

                    if (existingRecipe != null)
                    {
                        // If updateImages is true and recipe has no image, add one
                        if (updateImages && string.IsNullOrEmpty(existingRecipe.ImageUrl))
                        {
                            if (!dryRun)
                            {
                                var (success, error) = await UpdateRecipeImageAsync(httpClient, pexelsApiKey, existingRecipe, template.PexelsSearchTerm);
                                if (success)
                                {
                                    result.ImagesDownloaded++;
                                    result.CreatedRecipes.Add($"[UPDATED IMAGE] {template.Title}");
                                }
                                else
                                {
                                    result.SkippedReasons.Add($"{template.Title}: {error}");
                                }
                            }
                            else
                            {
                                result.CreatedRecipes.Add($"[DRY RUN] Would update image: {template.Title}");
                            }
                            processedCount++;
                            await Task.Delay(600); // Rate limiting
                            continue;
                        }

                        result.RecipesSkipped++;
                        result.SkippedReasons.Add($"{template.Title}: Already exists" + (string.IsNullOrEmpty(existingRecipe.ImageUrl) ? " (no image)" : " (has image)"));
                        continue;
                    }

                    if (dryRun)
                    {
                        result.CreatedRecipes.Add($"[DRY RUN] Would create: {template.Title}");
                        processedCount++;
                        continue;
                    }

                    // Download image from Pexels
                    string? imageUrl = null;
                    string? imageBlobName = null;
                    string? photoCredit = null;
                    string? photoCreditUrl = null;

                    try
                    {
                        var pexelsImage = await SearchPexelsImageAsync(httpClient, pexelsApiKey, template.PexelsSearchTerm);
                        if (pexelsImage != null)
                        {
                            var imageData = await DownloadImageAsync(httpClient, pexelsImage.Url);
                            if (imageData != null)
                            {
                                var fileName = $"seed_{Guid.NewGuid()}.jpeg";
                                imageUrl = await UploadImageToBlobAsync(imageData, fileName);
                                imageBlobName = fileName;
                                photoCredit = pexelsImage.Photographer;
                                photoCreditUrl = pexelsImage.PhotographerUrl;
                                result.ImagesDownloaded++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get image for {Recipe}", template.Title);
                    }

                    // Create recipe
                    var recipe = new Recipe
                    {
                        Id = Guid.NewGuid(),
                        Title = template.Title,
                        Description = template.Description,
                        Instructions = template.Instructions,
                        Region = template.Region,
                        ImageUrl = imageUrl,
                        ImageBlobName = imageBlobName,
                        PhotoCredit = photoCredit,
                        PhotoCreditUrl = photoCreditUrl,
                        PrepTimeMinutes = template.PrepTimeMinutes,
                        CookTimeMinutes = template.CookTimeMinutes,
                        Difficulty = template.Difficulty,
                        UserId = systemUser.Id,
                        CreatedAt = DateTime.UtcNow
                    };

                    // Add ingredients
                    var ingredients = await CreateIngredientsAsync(template.Ingredients, recipe.Id);
                    recipe.Ingredients = ingredients;
                    result.IngredientsCreated += ingredients.Count;

                    // Add tags
                    await AddTagsToRecipeAsync(recipe, template.Tags);

                    _db.Recipes.Add(recipe);
                    await _db.SaveChangesAsync();

                    result.RecipesCreated++;
                    result.CreatedRecipes.Add(template.Title);
                    processedCount++;

                    _logger.LogInformation("Created recipe: {Name} (Photo by {Photographer})",
                        template.Title, photoCredit ?? "N/A");

                    // Rate limiting - be nice to Pexels API
                    await Task.Delay(600);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create recipe: {Name}", template.Title);
                    result.Errors.Add($"{template.Title}: {ex.Message}");
                }
            }

            return result;
        }

        private async Task<User?> GetOrCreateSystemUserAsync(bool dryRun)
        {
            var systemUser = await _db.Users.FirstOrDefaultAsync(u => u.Username == "Platerly");

            // Get password from config (set a secure password in appsettings or Azure)
            var systemPassword = _config["Seeder:PlaterlyPassword"] ?? "Platerly2024!";

            if (systemUser != null)
            {
                // Update password to match config (always update to ensure it's current)
                systemUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(systemPassword);
                await _db.SaveChangesAsync();
                _logger.LogInformation("Platerly account password synced with config");
                return systemUser;
            }

            if (dryRun) return new User { Id = Guid.Empty, Username = "Platerly" };

            systemUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "Platerly",
                Email = "recipes@platerly.com",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(systemPassword),
                DisplayName = "Platerly Kitchen",
                Bio = "Official Platerly recipes - curated halal dishes from around the world.",
                IsEmailVerified = true,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(systemUser);
            await _db.SaveChangesAsync();
            _logger.LogInformation("Created Platerly account with configured password");
            return systemUser;
        }

        private async Task<(bool Success, string Error)> UpdateRecipeImageAsync(HttpClient httpClient, string pexelsApiKey, Recipe recipe, string searchTerm)
        {
            try
            {
                _logger.LogInformation("Searching Pexels for: {SearchTerm}", searchTerm);
                var pexelsImage = await SearchPexelsImageAsync(httpClient, pexelsApiKey, searchTerm);
                if (pexelsImage == null)
                {
                    return (false, "Pexels returned no image");
                }

                _logger.LogInformation("Found Pexels image: {Url}", pexelsImage.Url);
                var imageData = await DownloadImageAsync(httpClient, pexelsImage.Url);
                if (imageData == null)
                {
                    return (false, "Failed to download from Pexels");
                }

                _logger.LogInformation("Downloaded {Size} bytes, uploading to blob...", imageData.Length);
                var fileName = $"seed_{Guid.NewGuid()}.jpeg";
                var imageUrl = await UploadImageToBlobAsync(imageData, fileName);
                if (string.IsNullOrEmpty(imageUrl))
                {
                    return (false, "Failed to upload to blob storage");
                }

                recipe.ImageUrl = imageUrl;
                recipe.ImageBlobName = fileName;
                recipe.PhotoCredit = pexelsImage.Photographer;
                recipe.PhotoCreditUrl = pexelsImage.PhotographerUrl;

                await _db.SaveChangesAsync();
                _logger.LogInformation("Successfully updated image for: {Title}", recipe.Title);
                return (true, "");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating image for recipe: {Title}", recipe.Title);
                return (false, ex.Message);
            }
        }

        private async Task<PexelsImageResult?> SearchPexelsImageAsync(HttpClient client, string apiKey, string query)
        {
            try
            {
                var searchQuery = Uri.EscapeDataString(query);
                var url = $"https://api.pexels.com/v1/search?query={searchQuery}&per_page=1&orientation=landscape";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", apiKey);

                var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Pexels API returned {Status} for query: {Query}. Response: {Body}",
                        response.StatusCode, query, errorBody);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Pexels response for {Query}: {Json}", query, json.Length > 500 ? json.Substring(0, 500) + "..." : json);

                var data = JsonSerializer.Deserialize<PexelsResponse>(json);

                var photo = data?.Photos?.FirstOrDefault();
                if (photo == null)
                {
                    _logger.LogWarning("No photos found in Pexels response for query: {Query}", query);
                    return null;
                }

                return new PexelsImageResult
                {
                    Url = photo.Src?.Large2X ?? photo.Src?.Large ?? photo.Src?.Original ?? "",
                    Photographer = photo.Photographer ?? "Unknown",
                    PhotographerUrl = photo.PhotographerUrl ?? "https://www.pexels.com"
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pexels search failed for {Query}", query);
                return null;
            }
        }

        private async Task<byte[]?> DownloadImageAsync(HttpClient client, string url)
        {
            try
            {
                _logger.LogDebug("Downloading image from: {Url}", url);
                var data = await client.GetByteArrayAsync(url);
                _logger.LogDebug("Downloaded {Size} bytes", data.Length);
                return data;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download image from {Url}", url);
                return null;
            }
        }

        private async Task<string?> UploadImageToBlobAsync(byte[] imageData, string fileName)
        {
            try
            {
                _logger.LogDebug("Uploading image {FileName} ({Size} bytes)", fileName, imageData.Length);
                using var stream = new MemoryStream(imageData);
                stream.Position = 0; // Ensure stream is at beginning
                var formFile = new FormFile(stream, 0, imageData.Length, "image", fileName)
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "image/jpeg"
                };

                var url = await _blobService.UploadRecipeImageAsync(formFile, fileName);
                _logger.LogDebug("Uploaded to blob, URL: {Url}", url);
                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload image {FileName}", fileName);
                return null;
            }
        }

        private async Task<List<Ingredient>> CreateIngredientsAsync(List<IngredientTemplate> templates, Guid recipeId)
        {
            var ingredients = new List<Ingredient>();

            foreach (var template in templates)
            {
                if (string.IsNullOrWhiteSpace(template.Name)) continue;

                var normalizedName = template.Name.Trim().ToLowerInvariant();
                var globalIngredient = await _db.GlobalIngredients
                    .FirstOrDefaultAsync(g => g.NormalizedName == normalizedName);

                if (globalIngredient == null)
                {
                    globalIngredient = new GlobalIngredient
                    {
                        Id = Guid.NewGuid(),
                        Name = template.Name.Trim(),
                        NormalizedName = normalizedName,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    };
                    _db.GlobalIngredients.Add(globalIngredient);
                }

                ingredients.Add(new Ingredient
                {
                    Id = Guid.NewGuid(),
                    RecipeId = recipeId,
                    GlobalIngredientId = globalIngredient.Id,
                    Quantity = template.Quantity,
                    IsAllergenic = false
                });
            }

            return ingredients;
        }

        private async Task AddTagsToRecipeAsync(Recipe recipe, List<string> tagNames)
        {
            // Always add Halal tag
            var allTags = new List<string>(tagNames) { "Halal" };

            foreach (var tagName in allTags.Distinct())
            {
                var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Name == tagName);
                if (tag == null)
                {
                    tag = new Tag { Name = tagName };
                    _db.Tags.Add(tag);
                    await _db.SaveChangesAsync();
                }

                recipe.RecipeTags.Add(new RecipeTag { TagId = tag.Id });
            }
        }

        #region Curated Halal Recipes

        private List<RecipeTemplate> GetCuratedHalalRecipes()
        {
            return new List<RecipeTemplate>
            {
                // ===== MIDDLE EASTERN =====
                new RecipeTemplate
                {
                    Title = "Classic Chicken Shawarma",
                    Description = "Tender, aromatic chicken shawarma with warm spices. A beloved Middle Eastern street food that's perfect wrapped in pita with tahini sauce and fresh vegetables.",
                    Instructions = "1. Combine yogurt, lemon juice, olive oil, garlic, and all spices in a large bowl.\n2. Add chicken thighs and marinate for at least 2 hours, preferably overnight.\n3. Preheat oven to 425°F (220°C).\n4. Thread chicken onto skewers or spread on a baking sheet.\n5. Roast for 25-30 minutes until charred and cooked through.\n6. Let rest for 5 minutes, then slice thinly.\n7. Serve in warm pita bread with tahini, pickled turnips, and fresh vegetables.",
                    Region = "Middle Eastern",
                    PrepTimeMinutes = 20,
                    CookTimeMinutes = 30,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "chicken shawarma middle eastern food",
                    Tags = new List<string> { "Middle Eastern", "Chicken", "Dinner", "High Protein" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Chicken thighs", "1 kg"),
                        new("Plain yogurt", "1 cup"),
                        new("Lemon juice", "3 tablespoons"),
                        new("Olive oil", "2 tablespoons"),
                        new("Garlic", "4 cloves, minced"),
                        new("Cumin", "2 teaspoons"),
                        new("Paprika", "2 teaspoons"),
                        new("Turmeric", "1 teaspoon"),
                        new("Cinnamon", "1/2 teaspoon"),
                        new("Cardamom", "1/4 teaspoon"),
                        new("Salt", "1 teaspoon"),
                        new("Black pepper", "1/2 teaspoon"),
                        new("Pita bread", "6 pieces")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Creamy Hummus",
                    Description = "Silky smooth hummus made from scratch. This Middle Eastern dip is perfect with warm pita bread, vegetables, or as a spread in sandwiches.",
                    Instructions = "1. Drain and rinse the chickpeas thoroughly.\n2. Add chickpeas to a food processor with tahini, lemon juice, garlic, and salt.\n3. Process for 2 minutes until smooth.\n4. With processor running, slowly drizzle in ice water until desired consistency.\n5. Taste and adjust lemon juice and salt as needed.\n6. Transfer to a serving bowl, drizzle with olive oil.\n7. Sprinkle with paprika and serve with warm pita.",
                    Region = "Middle Eastern",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 0,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "hummus chickpea dip",
                    Tags = new List<string> { "Middle Eastern", "Vegetarian", "Vegan", "Appetizer", "Dip" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Chickpeas", "2 cans (400g each), drained"),
                        new("Tahini", "1/2 cup"),
                        new("Lemon juice", "1/4 cup"),
                        new("Garlic", "2 cloves"),
                        new("Salt", "1 teaspoon"),
                        new("Ice water", "2-4 tablespoons"),
                        new("Olive oil", "2 tablespoons"),
                        new("Paprika", "for garnish")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Crispy Falafel",
                    Description = "Crispy on the outside, tender and herb-filled on the inside. These authentic falafel are made from dried chickpeas for the perfect texture.",
                    Instructions = "1. Soak dried chickpeas overnight in plenty of water (do not use canned).\n2. Drain chickpeas and add to food processor with onion, garlic, herbs, and spices.\n3. Pulse until mixture resembles coarse sand - do not over-process.\n4. Transfer to bowl, cover, and refrigerate for 1 hour.\n5. Form mixture into small patties or balls.\n6. Heat oil to 350°F (175°C) for deep frying.\n7. Fry falafel in batches for 3-4 minutes until deep golden brown.\n8. Drain on paper towels and serve immediately.",
                    Region = "Middle Eastern",
                    PrepTimeMinutes = 30,
                    CookTimeMinutes = 15,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "falafel middle eastern",
                    Tags = new List<string> { "Middle Eastern", "Vegetarian", "Vegan", "Street Food" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Dried chickpeas", "2 cups"),
                        new("Onion", "1 medium"),
                        new("Garlic", "4 cloves"),
                        new("Fresh parsley", "1 cup"),
                        new("Fresh cilantro", "1/2 cup"),
                        new("Cumin", "1 teaspoon"),
                        new("Coriander", "1 teaspoon"),
                        new("Salt", "1 teaspoon"),
                        new("Black pepper", "1/4 teaspoon"),
                        new("Baking powder", "1/2 teaspoon"),
                        new("Vegetable oil", "for frying")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Lamb Kofta Kebabs",
                    Description = "Juicy, spiced lamb kebabs grilled to perfection. These Middle Eastern favorites are wonderful with rice, flatbread, and a cool yogurt sauce.",
                    Instructions = "1. Combine lamb mince with grated onion, garlic, and all spices in a large bowl.\n2. Mix well with hands until the mixture becomes sticky.\n3. Cover and refrigerate for 30 minutes.\n4. Form mixture around metal skewers into long sausage shapes.\n5. Preheat grill or grill pan to high heat.\n6. Grill kofta for 3-4 minutes per side until cooked through.\n7. Rest for 2 minutes before serving.\n8. Serve with flatbread, yogurt sauce, and fresh salad.",
                    Region = "Middle Eastern",
                    PrepTimeMinutes = 20,
                    CookTimeMinutes = 10,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "lamb kofta kebab grilled",
                    Tags = new List<string> { "Middle Eastern", "Lamb", "Dinner", "Grilled", "High Protein" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Lamb mince", "500g"),
                        new("Onion", "1 small, grated"),
                        new("Garlic", "3 cloves, minced"),
                        new("Fresh parsley", "1/4 cup, chopped"),
                        new("Cumin", "1 teaspoon"),
                        new("Coriander", "1 teaspoon"),
                        new("Paprika", "1/2 teaspoon"),
                        new("Cinnamon", "1/4 teaspoon"),
                        new("Salt", "1 teaspoon"),
                        new("Black pepper", "1/2 teaspoon")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Tabbouleh Salad",
                    Description = "A refreshing Lebanese salad bursting with fresh herbs, bulgur wheat, and bright lemon dressing. Perfect as a side dish or light meal.",
                    Instructions = "1. Soak bulgur in boiling water for 15-20 minutes until tender.\n2. Drain well and squeeze out excess water.\n3. Finely chop parsley, mint, tomatoes, and green onions.\n4. Combine bulgur with chopped vegetables and herbs.\n5. Whisk together lemon juice, olive oil, salt, and pepper.\n6. Pour dressing over salad and toss well.\n7. Refrigerate for 30 minutes to let flavors meld.\n8. Serve chilled or at room temperature.",
                    Region = "Lebanese",
                    PrepTimeMinutes = 25,
                    CookTimeMinutes = 0,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "tabbouleh parsley salad",
                    Tags = new List<string> { "Middle Eastern", "Lebanese", "Vegetarian", "Vegan", "Salad", "Healthy" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Fine bulgur wheat", "1/2 cup"),
                        new("Fresh parsley", "3 cups, finely chopped"),
                        new("Fresh mint", "1/2 cup, finely chopped"),
                        new("Tomatoes", "2 medium, diced"),
                        new("Green onions", "4, thinly sliced"),
                        new("Lemon juice", "1/4 cup"),
                        new("Olive oil", "1/4 cup"),
                        new("Salt", "1 teaspoon"),
                        new("Black pepper", "1/2 teaspoon")
                    }
                },

                // ===== SOUTH ASIAN =====
                new RecipeTemplate
                {
                    Title = "Chicken Biryani",
                    Description = "Aromatic layered rice dish with tender spiced chicken. This celebratory South Asian classic features fragrant basmati rice, warm spices, and caramelized onions.",
                    Instructions = "1. Marinate chicken in yogurt, ginger-garlic paste, and spices for 2 hours.\n2. Soak basmati rice for 30 minutes, then parboil until 70% cooked.\n3. Fry sliced onions until deep golden brown, set aside.\n4. In a heavy pot, cook marinated chicken until 80% done.\n5. Layer parboiled rice over chicken.\n6. Top with fried onions, saffron milk, ghee, and fresh herbs.\n7. Cover tightly and cook on low heat for 25 minutes.\n8. Let rest 5 minutes, then gently mix layers when serving.",
                    Region = "South Asian",
                    PrepTimeMinutes = 45,
                    CookTimeMinutes = 45,
                    Difficulty = Difficulty.Hard,
                    PexelsSearchTerm = "chicken biryani rice",
                    Tags = new List<string> { "South Asian", "Indian", "Pakistani", "Chicken", "Rice", "Dinner" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Chicken", "1 kg, cut into pieces"),
                        new("Basmati rice", "2 cups"),
                        new("Plain yogurt", "1 cup"),
                        new("Onions", "3 large, sliced"),
                        new("Ginger-garlic paste", "2 tablespoons"),
                        new("Biryani masala", "2 tablespoons"),
                        new("Cumin", "1 teaspoon"),
                        new("Coriander powder", "1 teaspoon"),
                        new("Turmeric", "1/2 teaspoon"),
                        new("Red chili powder", "1 teaspoon"),
                        new("Saffron", "a pinch"),
                        new("Warm milk", "1/4 cup"),
                        new("Ghee", "4 tablespoons"),
                        new("Fresh cilantro", "1/2 cup"),
                        new("Fresh mint", "1/4 cup"),
                        new("Salt", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Butter Chicken",
                    Description = "Creamy, mildly spiced tomato-based curry with tender chicken pieces. This beloved North Indian dish is perfect with naan bread or basmati rice.",
                    Instructions = "1. Marinate chicken in yogurt, lemon juice, and spices for at least 1 hour.\n2. Grill or pan-fry chicken until lightly charred, set aside.\n3. In a large pan, melt butter and sauté ginger-garlic paste.\n4. Add tomato puree and cook for 10 minutes until oil separates.\n5. Add spices and cook for 2 minutes.\n6. Stir in cream and kasuri methi.\n7. Add cooked chicken and simmer for 10 minutes.\n8. Finish with a swirl of cream and serve hot.",
                    Region = "North Indian",
                    PrepTimeMinutes = 30,
                    CookTimeMinutes = 30,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "butter chicken curry",
                    Tags = new List<string> { "South Asian", "Indian", "Chicken", "Curry", "Dinner", "Creamy" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Chicken breast", "500g, cubed"),
                        new("Plain yogurt", "1/2 cup"),
                        new("Lemon juice", "2 tablespoons"),
                        new("Butter", "4 tablespoons"),
                        new("Ginger-garlic paste", "2 tablespoons"),
                        new("Tomato puree", "2 cups"),
                        new("Heavy cream", "1 cup"),
                        new("Garam masala", "1 teaspoon"),
                        new("Cumin", "1 teaspoon"),
                        new("Coriander powder", "1 teaspoon"),
                        new("Red chili powder", "1/2 teaspoon"),
                        new("Kasuri methi", "1 tablespoon"),
                        new("Salt", "to taste"),
                        new("Sugar", "1 teaspoon")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Dal Tadka",
                    Description = "Comforting yellow lentils tempered with aromatic spices and ghee. This everyday Indian staple is nutritious, flavorful, and pairs perfectly with rice or roti.",
                    Instructions = "1. Wash lentils thoroughly and cook with turmeric until soft.\n2. Mash lentils lightly with a spoon.\n3. Heat ghee in a small pan for the tadka.\n4. Add cumin seeds and let them splutter.\n5. Add chopped garlic and sauté until golden.\n6. Add onions and cook until soft.\n7. Add tomatoes, green chili, and spices. Cook for 5 minutes.\n8. Pour tadka over cooked dal, garnish with cilantro.",
                    Region = "Indian",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 30,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "dal lentil curry indian",
                    Tags = new List<string> { "South Asian", "Indian", "Vegetarian", "Vegan", "Lentils", "Healthy" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Yellow lentils (toor dal)", "1 cup"),
                        new("Water", "3 cups"),
                        new("Turmeric", "1/2 teaspoon"),
                        new("Ghee", "3 tablespoons"),
                        new("Cumin seeds", "1 teaspoon"),
                        new("Garlic", "4 cloves, chopped"),
                        new("Onion", "1 medium, chopped"),
                        new("Tomato", "1 medium, chopped"),
                        new("Green chili", "1, slit"),
                        new("Red chili powder", "1/2 teaspoon"),
                        new("Fresh cilantro", "for garnish"),
                        new("Salt", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Vegetable Samosas",
                    Description = "Crispy, golden pastries filled with spiced potatoes and peas. These beloved South Asian snacks are perfect with mint chutney or tamarind sauce.",
                    Instructions = "1. Make dough: Mix flour, salt, and oil. Add water to form stiff dough. Rest 30 minutes.\n2. Boil and mash potatoes coarsely.\n3. Heat oil, add cumin seeds, then add peas and spices.\n4. Add potatoes, mix well, and let cool.\n5. Divide dough into balls, roll into ovals, cut in half.\n6. Form cones, fill with potato mixture, seal edges with water.\n7. Deep fry at 325°F until golden brown and crispy.\n8. Serve hot with chutney.",
                    Region = "South Asian",
                    PrepTimeMinutes = 45,
                    CookTimeMinutes = 20,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "samosa indian snack",
                    Tags = new List<string> { "South Asian", "Indian", "Vegetarian", "Snack", "Appetizer", "Street Food" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("All-purpose flour", "2 cups"),
                        new("Vegetable oil", "4 tablespoons + for frying"),
                        new("Potatoes", "3 medium, boiled"),
                        new("Green peas", "1/2 cup"),
                        new("Cumin seeds", "1 teaspoon"),
                        new("Ginger", "1 inch, minced"),
                        new("Green chili", "2, minced"),
                        new("Garam masala", "1 teaspoon"),
                        new("Coriander powder", "1 teaspoon"),
                        new("Amchur (dry mango powder)", "1/2 teaspoon"),
                        new("Fresh cilantro", "2 tablespoons"),
                        new("Salt", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Palak Paneer",
                    Description = "Creamy spinach curry with soft paneer cheese cubes. This North Indian vegetarian classic is rich in iron and protein, perfect with naan or rice.",
                    Instructions = "1. Blanch spinach in boiling water for 2 minutes, then ice bath.\n2. Blend spinach into a smooth puree.\n3. Cut paneer into cubes and lightly fry until golden. Set aside.\n4. Heat oil, add cumin seeds, then sauté onions until soft.\n5. Add ginger-garlic paste and cook for 2 minutes.\n6. Add tomatoes and cook until soft.\n7. Add spinach puree and spices, simmer for 10 minutes.\n8. Add paneer and cream, cook 5 more minutes. Serve hot.",
                    Region = "North Indian",
                    PrepTimeMinutes = 20,
                    CookTimeMinutes = 25,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "palak paneer spinach",
                    Tags = new List<string> { "South Asian", "Indian", "Vegetarian", "Paneer", "Spinach", "Dinner" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Fresh spinach", "500g"),
                        new("Paneer", "250g, cubed"),
                        new("Onion", "1 medium, chopped"),
                        new("Tomato", "1 medium, chopped"),
                        new("Ginger-garlic paste", "1 tablespoon"),
                        new("Cumin seeds", "1 teaspoon"),
                        new("Garam masala", "1/2 teaspoon"),
                        new("Red chili powder", "1/2 teaspoon"),
                        new("Heavy cream", "2 tablespoons"),
                        new("Oil", "3 tablespoons"),
                        new("Salt", "to taste")
                    }
                },

                // ===== MEDITERRANEAN & SEAFOOD =====
                new RecipeTemplate
                {
                    Title = "Grilled Salmon with Herbs",
                    Description = "Perfectly grilled salmon fillet with a fragrant herb crust. This healthy Mediterranean-style dish is elegant enough for guests yet simple enough for weeknights.",
                    Instructions = "1. Pat salmon fillets dry and season with salt and pepper.\n2. Mix olive oil with minced garlic, lemon zest, and fresh herbs.\n3. Brush herb mixture generously over salmon.\n4. Preheat grill to medium-high heat.\n5. Grill salmon skin-side down for 4-5 minutes.\n6. Carefully flip and grill 3-4 minutes more until just cooked.\n7. Remove when internal temp reaches 145°F.\n8. Serve with lemon wedges and extra fresh herbs.",
                    Region = "Mediterranean",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 10,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "grilled salmon herbs",
                    Tags = new List<string> { "Mediterranean", "Seafood", "Dinner", "Healthy", "High Protein", "Low Carb" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Salmon fillets", "4 pieces (150g each)"),
                        new("Olive oil", "3 tablespoons"),
                        new("Garlic", "3 cloves, minced"),
                        new("Lemon", "1, zested and juiced"),
                        new("Fresh dill", "2 tablespoons, chopped"),
                        new("Fresh parsley", "2 tablespoons, chopped"),
                        new("Salt", "1 teaspoon"),
                        new("Black pepper", "1/2 teaspoon")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Shrimp Scampi",
                    Description = "Succulent shrimp sautéed in garlic butter with white grape juice and fresh herbs. A quick, elegant dish that's perfect over pasta or with crusty bread.",
                    Instructions = "1. Season shrimp with salt and pepper.\n2. Heat olive oil and half the butter in a large skillet.\n3. Sauté shrimp for 1-2 minutes per side until pink. Remove and set aside.\n4. Add remaining butter and garlic to pan, cook 1 minute.\n5. Add white grape juice and lemon juice, simmer 2 minutes.\n6. Return shrimp to pan with red pepper flakes.\n7. Toss with fresh parsley.\n8. Serve immediately over linguine or with bread.",
                    Region = "Italian",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 10,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "shrimp scampi garlic",
                    Tags = new List<string> { "Mediterranean", "Italian", "Seafood", "Dinner", "Quick Meal" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Large shrimp", "500g, peeled and deveined"),
                        new("Butter", "4 tablespoons"),
                        new("Olive oil", "2 tablespoons"),
                        new("Garlic", "6 cloves, minced"),
                        new("White grape juice", "1/2 cup"),
                        new("Lemon juice", "3 tablespoons"),
                        new("Red pepper flakes", "1/4 teaspoon"),
                        new("Fresh parsley", "1/4 cup, chopped"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Greek Salad",
                    Description = "A refreshing Mediterranean salad with crisp vegetables, tangy feta cheese, and kalamata olives. Dressed simply with olive oil and oregano.",
                    Instructions = "1. Cut tomatoes into wedges and cucumber into half-moons.\n2. Slice red onion into thin rings.\n3. Cut bell pepper into strips.\n4. Combine all vegetables in a large bowl.\n5. Add kalamata olives.\n6. Place feta cheese block on top or crumble over salad.\n7. Drizzle generously with olive oil.\n8. Sprinkle with dried oregano, salt, and pepper. Serve immediately.",
                    Region = "Greek",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 0,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "greek salad feta",
                    Tags = new List<string> { "Mediterranean", "Greek", "Vegetarian", "Salad", "Healthy", "Low Carb" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Tomatoes", "4 medium, ripe"),
                        new("Cucumber", "1 large"),
                        new("Red onion", "1/2 medium"),
                        new("Green bell pepper", "1"),
                        new("Kalamata olives", "1/2 cup"),
                        new("Feta cheese", "200g"),
                        new("Extra virgin olive oil", "4 tablespoons"),
                        new("Dried oregano", "1 teaspoon"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Baked Fish with Tomatoes",
                    Description = "Tender white fish fillets baked on a bed of tomatoes, olives, and capers. A light, healthy Mediterranean dish bursting with flavor.",
                    Instructions = "1. Preheat oven to 400°F (200°C).\n2. Spread cherry tomatoes in a baking dish.\n3. Add olives, capers, and sliced garlic.\n4. Drizzle with half the olive oil, season with salt and pepper.\n5. Roast tomatoes for 15 minutes.\n6. Season fish fillets and place on top of tomatoes.\n7. Drizzle with remaining oil and lemon juice.\n8. Bake 12-15 minutes until fish flakes easily. Top with fresh basil.",
                    Region = "Mediterranean",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 30,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "baked fish tomatoes mediterranean",
                    Tags = new List<string> { "Mediterranean", "Seafood", "Dinner", "Healthy", "Low Carb" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("White fish fillets", "4 pieces (cod, sea bass, or halibut)"),
                        new("Cherry tomatoes", "2 cups, halved"),
                        new("Kalamata olives", "1/2 cup"),
                        new("Capers", "2 tablespoons"),
                        new("Garlic", "3 cloves, sliced"),
                        new("Olive oil", "4 tablespoons"),
                        new("Lemon juice", "2 tablespoons"),
                        new("Fresh basil", "1/4 cup"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste")
                    }
                },

                // ===== PASTA & RICE =====
                new RecipeTemplate
                {
                    Title = "Creamy Garlic Pasta",
                    Description = "Rich and creamy pasta with a luscious garlic parmesan sauce. A comforting vegetarian dish that comes together in under 30 minutes.",
                    Instructions = "1. Cook pasta according to package directions. Reserve 1 cup pasta water.\n2. Meanwhile, melt butter in a large pan over medium heat.\n3. Add minced garlic and cook for 1 minute until fragrant.\n4. Pour in heavy cream and bring to a gentle simmer.\n5. Add parmesan cheese and stir until melted and smooth.\n6. Season with salt, pepper, and a pinch of nutmeg.\n7. Toss drained pasta with sauce, adding pasta water as needed.\n8. Garnish with fresh parsley and extra parmesan.",
                    Region = "Italian",
                    PrepTimeMinutes = 5,
                    CookTimeMinutes = 20,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "creamy pasta garlic",
                    Tags = new List<string> { "Italian", "Pasta", "Vegetarian", "Dinner", "Comfort Food", "Quick Meal" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Fettuccine pasta", "400g"),
                        new("Butter", "4 tablespoons"),
                        new("Garlic", "6 cloves, minced"),
                        new("Heavy cream", "1.5 cups"),
                        new("Parmesan cheese", "1 cup, grated"),
                        new("Nutmeg", "a pinch"),
                        new("Fresh parsley", "2 tablespoons, chopped"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Vegetable Fried Rice",
                    Description = "Quick and flavorful fried rice loaded with colorful vegetables. A versatile dish that's perfect for using up leftover rice and veggies.",
                    Instructions = "1. Use day-old rice for best results (or spread fresh rice on a tray to cool).\n2. Heat oil in a wok or large pan over high heat.\n3. Scramble eggs, break into small pieces, set aside.\n4. Add more oil, stir-fry carrots and peas for 2 minutes.\n5. Add bell peppers and green onions, cook 1 minute.\n6. Add cold rice, breaking up any clumps.\n7. Drizzle soy sauce around edges of pan, toss everything together.\n8. Return eggs to pan, add sesame oil, and serve.",
                    Region = "Asian",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 10,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "vegetable fried rice",
                    Tags = new List<string> { "Asian", "Chinese", "Rice", "Vegetarian", "Quick Meal", "Dinner" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Cooked rice", "4 cups, cold"),
                        new("Eggs", "3"),
                        new("Carrots", "1 cup, diced"),
                        new("Green peas", "1/2 cup"),
                        new("Bell pepper", "1, diced"),
                        new("Green onions", "4, chopped"),
                        new("Soy sauce", "3 tablespoons"),
                        new("Sesame oil", "1 tablespoon"),
                        new("Vegetable oil", "3 tablespoons"),
                        new("Garlic", "2 cloves, minced"),
                        new("Salt", "to taste"),
                        new("White pepper", "1/4 teaspoon")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Mushroom Risotto",
                    Description = "Creamy, luxurious Italian risotto with earthy mushrooms and parmesan. A labor of love that results in the ultimate comfort food.",
                    Instructions = "1. Heat stock in a saucepan and keep warm.\n2. Sauté mushrooms in butter until golden, set aside.\n3. In a large pan, sauté onion in olive oil until soft.\n4. Add rice and toast for 2 minutes, stirring constantly.\n5. Add a ladle of warm stock, stir until absorbed.\n6. Continue adding stock one ladle at a time, stirring frequently.\n7. After 18-20 minutes, rice should be creamy but al dente.\n8. Stir in mushrooms, parmesan, and butter. Serve immediately.",
                    Region = "Italian",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 30,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "mushroom risotto",
                    Tags = new List<string> { "Italian", "Rice", "Vegetarian", "Dinner", "Comfort Food" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Arborio rice", "1.5 cups"),
                        new("Mixed mushrooms", "300g, sliced"),
                        new("Vegetable stock", "6 cups"),
                        new("Onion", "1 medium, finely diced"),
                        new("Garlic", "2 cloves, minced"),
                        new("Butter", "4 tablespoons"),
                        new("Olive oil", "2 tablespoons"),
                        new("Parmesan cheese", "3/4 cup, grated"),
                        new("Fresh thyme", "1 teaspoon"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste")
                    }
                },

                // ===== SOUPS & STEWS =====
                new RecipeTemplate
                {
                    Title = "Chicken Soup",
                    Description = "Hearty, nourishing chicken soup with vegetables and herbs. The ultimate comfort food that warms you from the inside out.",
                    Instructions = "1. Place whole chicken in a large pot with water, bring to boil.\n2. Skim foam, add onion, celery, carrots, and herbs.\n3. Simmer for 1 hour until chicken is cooked through.\n4. Remove chicken, shred meat, discard bones and skin.\n5. Strain broth, return to pot.\n6. Add diced vegetables and simmer until tender.\n7. Return shredded chicken to soup.\n8. Season with salt and pepper, garnish with fresh parsley.",
                    Region = "International",
                    PrepTimeMinutes = 20,
                    CookTimeMinutes = 90,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "chicken soup homemade",
                    Tags = new List<string> { "Soup", "Chicken", "Comfort Food", "Healthy", "Dinner" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Whole chicken", "1.5 kg"),
                        new("Carrots", "3, diced"),
                        new("Celery", "3 stalks, diced"),
                        new("Onion", "1 large, quartered"),
                        new("Garlic", "4 cloves"),
                        new("Bay leaves", "2"),
                        new("Fresh thyme", "4 sprigs"),
                        new("Fresh parsley", "1/4 cup, chopped"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste"),
                        new("Water", "12 cups")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Lentil Soup",
                    Description = "Hearty, nutritious lentil soup with warming spices. This budget-friendly soup is filling, healthy, and perfect for meal prep.",
                    Instructions = "1. Heat olive oil in a large pot over medium heat.\n2. Sauté onion, carrots, and celery until softened, about 5 minutes.\n3. Add garlic and spices, cook for 1 minute until fragrant.\n4. Add lentils, tomatoes, and vegetable broth.\n5. Bring to a boil, then reduce heat and simmer.\n6. Cook for 25-30 minutes until lentils are tender.\n7. Add spinach and cook until wilted.\n8. Finish with lemon juice and fresh herbs.",
                    Region = "Mediterranean",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 35,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "lentil soup",
                    Tags = new List<string> { "Soup", "Vegetarian", "Vegan", "Lentils", "Healthy", "Meal Prep" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Brown or green lentils", "1.5 cups"),
                        new("Onion", "1 large, diced"),
                        new("Carrots", "2, diced"),
                        new("Celery", "2 stalks, diced"),
                        new("Garlic", "4 cloves, minced"),
                        new("Diced tomatoes", "1 can (400g)"),
                        new("Vegetable broth", "6 cups"),
                        new("Cumin", "1 teaspoon"),
                        new("Coriander", "1/2 teaspoon"),
                        new("Spinach", "2 cups"),
                        new("Lemon juice", "2 tablespoons"),
                        new("Olive oil", "2 tablespoons"),
                        new("Salt", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Lamb Stew",
                    Description = "Rich, comforting lamb stew with tender meat and root vegetables. A hearty one-pot meal perfect for cold weather.",
                    Instructions = "1. Season lamb chunks with salt and pepper.\n2. Brown lamb in batches in a Dutch oven, set aside.\n3. Sauté onions until softened, add garlic.\n4. Add tomato paste and flour, stir for 1 minute.\n5. Pour in broth, scraping up browned bits.\n6. Return lamb to pot, add herbs and bay leaves.\n7. Cover and simmer for 1 hour.\n8. Add potatoes and carrots, cook 30 more minutes until tender.",
                    Region = "International",
                    PrepTimeMinutes = 20,
                    CookTimeMinutes = 100,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "lamb stew",
                    Tags = new List<string> { "Stew", "Lamb", "Dinner", "Comfort Food", "One Pot" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Lamb shoulder", "1 kg, cubed"),
                        new("Onions", "2 large, chopped"),
                        new("Garlic", "4 cloves, minced"),
                        new("Carrots", "3, chunked"),
                        new("Potatoes", "4 medium, chunked"),
                        new("Tomato paste", "2 tablespoons"),
                        new("Flour", "2 tablespoons"),
                        new("Beef broth", "4 cups"),
                        new("Fresh rosemary", "2 sprigs"),
                        new("Fresh thyme", "4 sprigs"),
                        new("Bay leaves", "2"),
                        new("Olive oil", "3 tablespoons"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste")
                    }
                },

                // ===== BREAKFAST =====
                new RecipeTemplate
                {
                    Title = "Shakshuka",
                    Description = "Eggs poached in a spiced tomato and pepper sauce. This North African breakfast dish is perfect for brunch and best enjoyed with crusty bread.",
                    Instructions = "1. Heat olive oil in a large skillet over medium heat.\n2. Sauté onion and bell peppers until softened.\n3. Add garlic and cook for 1 minute.\n4. Add tomatoes, spices, and a pinch of sugar.\n5. Simmer for 10 minutes until slightly thickened.\n6. Make wells in the sauce and crack eggs into them.\n7. Cover and cook for 5-8 minutes until whites are set.\n8. Garnish with feta and fresh herbs. Serve immediately.",
                    Region = "North African",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 25,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "shakshuka eggs tomato",
                    Tags = new List<string> { "Middle Eastern", "North African", "Breakfast", "Eggs", "Vegetarian" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Eggs", "6"),
                        new("Canned tomatoes", "2 cans (400g each)"),
                        new("Red bell pepper", "1, diced"),
                        new("Onion", "1 medium, diced"),
                        new("Garlic", "4 cloves, minced"),
                        new("Cumin", "1 teaspoon"),
                        new("Paprika", "1 teaspoon"),
                        new("Cayenne pepper", "1/4 teaspoon"),
                        new("Feta cheese", "1/2 cup, crumbled"),
                        new("Fresh parsley", "for garnish"),
                        new("Olive oil", "2 tablespoons"),
                        new("Salt", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Fluffy Pancakes",
                    Description = "Light, fluffy American-style pancakes perfect for a weekend breakfast. Serve with maple syrup, fresh berries, or your favorite toppings.",
                    Instructions = "1. Whisk flour, sugar, baking powder, and salt in a large bowl.\n2. In another bowl, whisk milk, egg, melted butter, and vanilla.\n3. Pour wet ingredients into dry, mix until just combined (lumps are okay).\n4. Let batter rest for 5 minutes.\n5. Heat a non-stick pan over medium heat, lightly grease.\n6. Pour 1/4 cup batter per pancake.\n7. Cook until bubbles form on surface, flip and cook 1-2 more minutes.\n8. Serve warm with maple syrup and butter.",
                    Region = "American",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 15,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "pancakes breakfast stack",
                    Tags = new List<string> { "American", "Breakfast", "Vegetarian", "Sweet" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("All-purpose flour", "1.5 cups"),
                        new("Sugar", "2 tablespoons"),
                        new("Baking powder", "2 teaspoons"),
                        new("Salt", "1/2 teaspoon"),
                        new("Milk", "1.25 cups"),
                        new("Egg", "1 large"),
                        new("Butter", "3 tablespoons, melted"),
                        new("Vanilla extract", "1 teaspoon"),
                        new("Maple syrup", "for serving")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Turkish Menemen",
                    Description = "Scrambled eggs with tomatoes, peppers, and Turkish spices. A beloved Turkish breakfast dish that's both simple and satisfying.",
                    Instructions = "1. Heat olive oil in a pan over medium heat.\n2. Add chopped peppers and cook until slightly softened.\n3. Add tomatoes and cook until they break down, about 5 minutes.\n4. Season with salt, pepper, and red pepper flakes.\n5. Reduce heat to low, make space in pan.\n6. Crack eggs directly into the tomato mixture.\n7. Gently scramble, folding eggs with tomatoes until just set.\n8. Serve immediately with crusty bread.",
                    Region = "Turkish",
                    PrepTimeMinutes = 5,
                    CookTimeMinutes = 15,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "menemen turkish eggs",
                    Tags = new List<string> { "Turkish", "Middle Eastern", "Breakfast", "Eggs", "Vegetarian" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Eggs", "4"),
                        new("Tomatoes", "3 medium, chopped"),
                        new("Green bell pepper", "1, chopped"),
                        new("Green chili", "1, chopped (optional)"),
                        new("Onion", "1/2, chopped"),
                        new("Olive oil", "2 tablespoons"),
                        new("Red pepper flakes", "1/2 teaspoon"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste"),
                        new("Fresh parsley", "for garnish")
                    }
                },

                // ===== DESSERTS =====
                new RecipeTemplate
                {
                    Title = "Baklava",
                    Description = "Layers of crispy phyllo pastry with spiced nuts and sweet honey syrup. This iconic Middle Eastern dessert is rich, nutty, and absolutely irresistible.",
                    Instructions = "1. Preheat oven to 350°F (175°C).\n2. Mix chopped nuts with cinnamon and a pinch of salt.\n3. Brush a baking pan with melted butter.\n4. Layer 8 sheets of phyllo, brushing each with butter.\n5. Spread 1/3 of nut mixture, add 4 more buttered sheets.\n6. Repeat layers, ending with 8 phyllo sheets on top.\n7. Cut into diamonds, bake 45-50 minutes until golden.\n8. Make syrup: boil sugar, water, honey, and lemon. Pour hot syrup over hot baklava.",
                    Region = "Middle Eastern",
                    PrepTimeMinutes = 30,
                    CookTimeMinutes = 50,
                    Difficulty = Difficulty.Hard,
                    PexelsSearchTerm = "baklava dessert",
                    Tags = new List<string> { "Middle Eastern", "Dessert", "Sweet", "Vegetarian" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Phyllo dough", "1 package (450g)"),
                        new("Walnuts", "1.5 cups, finely chopped"),
                        new("Pistachios", "1 cup, finely chopped"),
                        new("Butter", "1 cup, melted"),
                        new("Cinnamon", "1 teaspoon"),
                        new("Sugar", "1 cup"),
                        new("Water", "1 cup"),
                        new("Honey", "1/2 cup"),
                        new("Lemon juice", "1 tablespoon"),
                        new("Vanilla extract", "1 teaspoon")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Rice Pudding",
                    Description = "Creamy, comforting rice pudding with vanilla and cinnamon. This classic dessert is simple, nostalgic, and can be served warm or cold.",
                    Instructions = "1. Rinse rice and add to a pot with water. Boil until water is absorbed.\n2. Add milk and bring to a gentle simmer.\n3. Cook for 25-30 minutes, stirring frequently.\n4. Add sugar, vanilla, and a pinch of salt.\n5. Continue cooking until rice is very tender and creamy.\n6. Remove from heat, stir in butter.\n7. Serve warm or chilled.\n8. Dust with cinnamon and top with raisins if desired.",
                    Region = "International",
                    PrepTimeMinutes = 5,
                    CookTimeMinutes = 40,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "rice pudding cinnamon",
                    Tags = new List<string> { "Dessert", "Sweet", "Vegetarian", "Comfort Food" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Short-grain rice", "1 cup"),
                        new("Water", "2 cups"),
                        new("Whole milk", "3 cups"),
                        new("Sugar", "1/2 cup"),
                        new("Vanilla extract", "1 teaspoon"),
                        new("Butter", "2 tablespoons"),
                        new("Cinnamon", "for dusting"),
                        new("Raisins", "optional"),
                        new("Salt", "a pinch")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Mango Lassi",
                    Description = "Refreshing Indian yogurt drink blended with sweet mango. A cooling, creamy beverage perfect alongside spicy food or as a sweet treat.",
                    Instructions = "1. Add mango chunks to a blender.\n2. Add yogurt, milk, and sugar.\n3. Add cardamom for authentic flavor.\n4. Blend until completely smooth.\n5. Taste and adjust sweetness if needed.\n6. Add ice cubes and blend again until frothy.\n7. Pour into tall glasses.\n8. Garnish with a sprinkle of cardamom or mint leaves.",
                    Region = "Indian",
                    PrepTimeMinutes = 5,
                    CookTimeMinutes = 0,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "mango lassi drink",
                    Tags = new List<string> { "South Asian", "Indian", "Beverage", "Sweet", "Vegetarian" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Ripe mango", "2 cups, cubed"),
                        new("Plain yogurt", "1 cup"),
                        new("Milk", "1/2 cup"),
                        new("Sugar", "2-3 tablespoons"),
                        new("Ground cardamom", "1/4 teaspoon"),
                        new("Ice cubes", "1 cup"),
                        new("Fresh mint", "for garnish")
                    }
                },

                // ===== MORE CHICKEN =====
                new RecipeTemplate
                {
                    Title = "Chicken Tikka Masala",
                    Description = "Tender chicken pieces in a rich, creamy tomato-based sauce. This restaurant favorite is packed with aromatic spices and perfect with naan.",
                    Instructions = "1. Marinate chicken in yogurt, lemon juice, and tikka spices for 2+ hours.\n2. Thread onto skewers and grill until charred.\n3. For sauce, sauté onion until golden.\n4. Add ginger-garlic paste, cook 2 minutes.\n5. Add tomato puree and spices, simmer 15 minutes.\n6. Stir in cream and kasuri methi.\n7. Add grilled chicken pieces.\n8. Simmer 10 minutes, garnish with cilantro.",
                    Region = "Indian",
                    PrepTimeMinutes = 30,
                    CookTimeMinutes = 35,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "chicken tikka masala",
                    Tags = new List<string> { "South Asian", "Indian", "Chicken", "Curry", "Dinner" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Chicken breast", "600g, cubed"),
                        new("Plain yogurt", "1 cup"),
                        new("Lemon juice", "2 tablespoons"),
                        new("Garam masala", "2 teaspoons"),
                        new("Cumin", "1 teaspoon"),
                        new("Paprika", "1 teaspoon"),
                        new("Turmeric", "1/2 teaspoon"),
                        new("Onion", "1 large, diced"),
                        new("Ginger-garlic paste", "2 tablespoons"),
                        new("Tomato puree", "2 cups"),
                        new("Heavy cream", "1 cup"),
                        new("Kasuri methi", "1 tablespoon"),
                        new("Oil", "3 tablespoons"),
                        new("Salt", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Lemon Herb Roast Chicken",
                    Description = "Perfectly roasted whole chicken with crispy skin and juicy meat. Infused with lemon, garlic, and fresh herbs for incredible flavor.",
                    Instructions = "1. Preheat oven to 425°F (220°C).\n2. Pat chicken dry and season inside and out with salt and pepper.\n3. Stuff cavity with lemon halves, garlic, and herb sprigs.\n4. Rub butter under and over the skin.\n5. Tie legs together and tuck wings under.\n6. Place on a rack in roasting pan.\n7. Roast for 1 hour 15 minutes until internal temp reaches 165°F.\n8. Rest 15 minutes before carving.",
                    Region = "International",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 75,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "roast chicken herbs",
                    Tags = new List<string> { "Chicken", "Dinner", "Roasted", "Family Meal" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Whole chicken", "1.5 kg"),
                        new("Lemon", "1, halved"),
                        new("Garlic", "1 head, halved"),
                        new("Butter", "4 tablespoons, softened"),
                        new("Fresh rosemary", "4 sprigs"),
                        new("Fresh thyme", "6 sprigs"),
                        new("Olive oil", "2 tablespoons"),
                        new("Salt", "1 tablespoon"),
                        new("Black pepper", "1 teaspoon")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Chicken Stir Fry",
                    Description = "Quick and healthy chicken stir fry with colorful vegetables. A weeknight dinner winner that's ready in under 30 minutes.",
                    Instructions = "1. Slice chicken into thin strips, season with salt and cornstarch.\n2. Mix sauce ingredients in a small bowl.\n3. Heat wok or large pan over high heat with oil.\n4. Stir fry chicken until just cooked, remove and set aside.\n5. Add more oil, stir fry vegetables for 2-3 minutes.\n6. Return chicken to pan.\n7. Pour sauce over and toss until everything is coated.\n8. Serve immediately over steamed rice.",
                    Region = "Asian",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 10,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "chicken stir fry vegetables",
                    Tags = new List<string> { "Asian", "Chinese", "Chicken", "Dinner", "Quick Meal", "Healthy" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Chicken breast", "400g, sliced"),
                        new("Broccoli", "2 cups, florets"),
                        new("Bell peppers", "2, sliced"),
                        new("Carrots", "1, sliced thin"),
                        new("Soy sauce", "3 tablespoons"),
                        new("Oyster sauce", "2 tablespoons"),
                        new("Garlic", "3 cloves, minced"),
                        new("Ginger", "1 inch, minced"),
                        new("Cornstarch", "1 tablespoon"),
                        new("Vegetable oil", "3 tablespoons"),
                        new("Sesame oil", "1 teaspoon")
                    }
                },

                // ===== VEGETARIAN =====
                new RecipeTemplate
                {
                    Title = "Stuffed Bell Peppers",
                    Description = "Colorful bell peppers filled with spiced rice, vegetables, and herbs. A beautiful and satisfying vegetarian main course.",
                    Instructions = "1. Preheat oven to 375°F (190°C).\n2. Cut tops off peppers, remove seeds and membranes.\n3. Cook rice according to package directions.\n4. Sauté onion and garlic until soft.\n5. Mix rice with sautéed vegetables, tomatoes, herbs, and cheese.\n6. Season filling with salt, pepper, and spices.\n7. Stuff peppers with rice mixture, place in baking dish.\n8. Bake 35-40 minutes until peppers are tender.",
                    Region = "Mediterranean",
                    PrepTimeMinutes = 20,
                    CookTimeMinutes = 40,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "stuffed bell peppers",
                    Tags = new List<string> { "Vegetarian", "Mediterranean", "Dinner", "Healthy" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Bell peppers", "6 large, various colors"),
                        new("Rice", "1 cup, uncooked"),
                        new("Onion", "1 medium, diced"),
                        new("Garlic", "3 cloves, minced"),
                        new("Diced tomatoes", "1 can"),
                        new("Feta cheese", "1/2 cup, crumbled"),
                        new("Fresh parsley", "1/4 cup, chopped"),
                        new("Fresh mint", "2 tablespoons, chopped"),
                        new("Cumin", "1 teaspoon"),
                        new("Olive oil", "2 tablespoons"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Eggplant Parmesan",
                    Description = "Layers of breaded eggplant, marinara sauce, and melted cheese. A hearty Italian-American vegetarian classic that satisfies like no other.",
                    Instructions = "1. Slice eggplant into 1/2 inch rounds, salt and let drain 30 minutes.\n2. Preheat oven to 375°F (190°C).\n3. Set up breading station: flour, beaten eggs, breadcrumbs with parmesan.\n4. Pat eggplant dry, dredge in flour, egg, then breadcrumbs.\n5. Fry eggplant until golden on both sides.\n6. Spread sauce in baking dish, layer eggplant, mozzarella, and more sauce.\n7. Repeat layers, top with remaining cheese.\n8. Bake 25-30 minutes until bubbly and golden.",
                    Region = "Italian",
                    PrepTimeMinutes = 45,
                    CookTimeMinutes = 30,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "eggplant parmesan",
                    Tags = new List<string> { "Italian", "Vegetarian", "Dinner", "Comfort Food" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Eggplant", "2 large"),
                        new("Marinara sauce", "3 cups"),
                        new("Mozzarella cheese", "2 cups, shredded"),
                        new("Parmesan cheese", "1 cup, grated"),
                        new("Eggs", "3, beaten"),
                        new("Breadcrumbs", "2 cups"),
                        new("All-purpose flour", "1 cup"),
                        new("Fresh basil", "1/4 cup, chopped"),
                        new("Olive oil", "for frying"),
                        new("Salt", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Chickpea Curry",
                    Description = "A flavorful, protein-rich vegan curry with chickpeas in a spiced tomato-coconut sauce. Hearty, healthy, and incredibly satisfying.",
                    Instructions = "1. Heat oil in a large pan over medium heat.\n2. Sauté onion until golden, about 8 minutes.\n3. Add garlic, ginger, and spices. Cook 1 minute.\n4. Add tomatoes and cook until they break down.\n5. Add coconut milk and bring to a simmer.\n6. Add chickpeas and simmer for 15 minutes.\n7. Season with salt and add spinach if using.\n8. Garnish with cilantro and serve with rice or naan.",
                    Region = "Indian",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 30,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "chickpea curry",
                    Tags = new List<string> { "South Asian", "Indian", "Vegan", "Vegetarian", "Curry", "Healthy" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Chickpeas", "2 cans, drained"),
                        new("Coconut milk", "1 can"),
                        new("Diced tomatoes", "1 can"),
                        new("Onion", "1 large, diced"),
                        new("Garlic", "4 cloves, minced"),
                        new("Ginger", "1 inch, minced"),
                        new("Garam masala", "2 teaspoons"),
                        new("Cumin", "1 teaspoon"),
                        new("Turmeric", "1/2 teaspoon"),
                        new("Cayenne", "1/4 teaspoon"),
                        new("Fresh cilantro", "for garnish"),
                        new("Vegetable oil", "2 tablespoons"),
                        new("Salt", "to taste")
                    }
                },

                // ===== SIDES =====
                new RecipeTemplate
                {
                    Title = "Garlic Naan",
                    Description = "Soft, pillowy Indian flatbread brushed with garlic butter. Perfect for scooping up curries and dals.",
                    Instructions = "1. Mix flour, sugar, salt, and yeast in a bowl.\n2. Add yogurt, milk, and oil. Knead into smooth dough.\n3. Cover and let rise for 1-2 hours until doubled.\n4. Divide into 8 balls, roll each into oval shape.\n5. Heat a cast iron skillet over high heat.\n6. Cook naan 1-2 minutes per side until puffed and charred.\n7. Brush immediately with garlic butter.\n8. Sprinkle with cilantro if desired. Serve warm.",
                    Region = "Indian",
                    PrepTimeMinutes = 20,
                    CookTimeMinutes = 15,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "naan bread indian",
                    Tags = new List<string> { "South Asian", "Indian", "Bread", "Side Dish", "Vegetarian" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("All-purpose flour", "3 cups"),
                        new("Instant yeast", "1 teaspoon"),
                        new("Sugar", "1 teaspoon"),
                        new("Salt", "1 teaspoon"),
                        new("Plain yogurt", "1/2 cup"),
                        new("Warm milk", "1/2 cup"),
                        new("Vegetable oil", "2 tablespoons"),
                        new("Butter", "4 tablespoons, melted"),
                        new("Garlic", "4 cloves, minced"),
                        new("Fresh cilantro", "optional")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Roasted Vegetables",
                    Description = "A colorful medley of vegetables roasted to caramelized perfection. Simple, healthy, and endlessly versatile.",
                    Instructions = "1. Preheat oven to 425°F (220°C).\n2. Cut all vegetables into similar-sized pieces.\n3. Toss vegetables with olive oil, salt, and pepper.\n4. Add garlic and herbs.\n5. Spread in single layer on baking sheets (don't overcrowd).\n6. Roast for 25-30 minutes, stirring halfway.\n7. Vegetables should be tender and caramelized.\n8. Finish with a squeeze of lemon if desired.",
                    Region = "International",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 30,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "roasted vegetables colorful",
                    Tags = new List<string> { "Side Dish", "Vegetarian", "Vegan", "Healthy", "Low Carb" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Zucchini", "2, chunked"),
                        new("Bell peppers", "2, chunked"),
                        new("Red onion", "1, chunked"),
                        new("Cherry tomatoes", "1 cup"),
                        new("Broccoli", "2 cups florets"),
                        new("Olive oil", "4 tablespoons"),
                        new("Garlic", "4 cloves, minced"),
                        new("Fresh rosemary", "1 tablespoon"),
                        new("Fresh thyme", "1 tablespoon"),
                        new("Salt", "1 teaspoon"),
                        new("Black pepper", "1/2 teaspoon")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Cucumber Raita",
                    Description = "Cool, refreshing yogurt sauce with cucumber and mint. The perfect cooling accompaniment to spicy Indian dishes.",
                    Instructions = "1. Grate or finely dice cucumber.\n2. Salt cucumber lightly and let drain for 10 minutes.\n3. Squeeze out excess water from cucumber.\n4. Whisk yogurt until smooth.\n5. Add cucumber, mint, and cumin to yogurt.\n6. Season with salt to taste.\n7. Add a pinch of cayenne if desired.\n8. Chill for 30 minutes before serving.",
                    Region = "Indian",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 0,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "raita yogurt cucumber",
                    Tags = new List<string> { "South Asian", "Indian", "Side Dish", "Condiment", "Vegetarian" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Plain yogurt", "2 cups"),
                        new("Cucumber", "1 large"),
                        new("Fresh mint", "2 tablespoons, chopped"),
                        new("Cumin", "1/2 teaspoon, roasted"),
                        new("Salt", "to taste"),
                        new("Cayenne pepper", "a pinch, optional")
                    }
                },

                // ===== TURKISH / SOMALI =====
                new RecipeTemplate
                {
                    Title = "Turkish Lentil Soup (Mercimek)",
                    Description = "Velvety smooth red lentil soup, a staple of Turkish cuisine. Warming, nutritious, and traditionally served with a squeeze of lemon.",
                    Instructions = "1. Sauté onion and carrot in butter until soft.\n2. Add lentils, tomato paste, and spices. Stir well.\n3. Add broth and bring to boil.\n4. Reduce heat and simmer 25-30 minutes until lentils are very soft.\n5. Blend soup until completely smooth.\n6. Adjust consistency with more broth if needed.\n7. Season with salt and pepper.\n8. Serve with lemon wedges, dried mint, and Aleppo pepper.",
                    Region = "Turkish",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 35,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "turkish lentil soup",
                    Tags = new List<string> { "Turkish", "Soup", "Vegan", "Vegetarian", "Lentils", "Healthy" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Red lentils", "1.5 cups"),
                        new("Onion", "1 medium, diced"),
                        new("Carrot", "1, diced"),
                        new("Tomato paste", "1 tablespoon"),
                        new("Vegetable broth", "6 cups"),
                        new("Butter", "2 tablespoons"),
                        new("Cumin", "1 teaspoon"),
                        new("Paprika", "1/2 teaspoon"),
                        new("Dried mint", "for serving"),
                        new("Lemon", "for serving"),
                        new("Salt", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Somali Rice (Bariis)",
                    Description = "Fragrant Somali spiced rice with aromatic whole spices. A flavorful side dish that pairs perfectly with stews and grilled meats.",
                    Instructions = "1. Rinse rice until water runs clear, soak 30 minutes.\n2. Heat oil in a pot, add whole spices and sauté 1 minute.\n3. Add onion and cook until golden.\n4. Add raisins and carrots if using.\n5. Drain rice and add to pot, stir to coat with oil.\n6. Add hot water or broth, salt, and turmeric.\n7. Bring to boil, then cover and cook on low 15-18 minutes.\n8. Fluff with fork and serve.",
                    Region = "Somali",
                    PrepTimeMinutes = 35,
                    CookTimeMinutes = 25,
                    Difficulty = Difficulty.Medium,
                    PexelsSearchTerm = "spiced rice pilaf",
                    Tags = new List<string> { "Somali", "African", "Rice", "Side Dish" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Basmati rice", "2 cups"),
                        new("Vegetable oil", "3 tablespoons"),
                        new("Onion", "1 medium, diced"),
                        new("Cinnamon stick", "1"),
                        new("Cardamom pods", "4"),
                        new("Cloves", "4"),
                        new("Cumin seeds", "1 teaspoon"),
                        new("Turmeric", "1/2 teaspoon"),
                        new("Raisins", "1/4 cup, optional"),
                        new("Hot water", "3.5 cups"),
                        new("Salt", "1 teaspoon")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Somali Suqaar",
                    Description = "Tender cubed meat sautéed with peppers and Somali spices. A quick, flavorful dish often served with rice, pasta, or flatbread.",
                    Instructions = "1. Season meat cubes with salt, pepper, and xawaash spice.\n2. Heat oil in a large pan over high heat.\n3. Brown meat in batches, don't overcrowd. Set aside.\n4. Sauté onion and peppers until slightly softened.\n5. Add garlic and cook 1 minute.\n6. Return meat to pan with tomatoes.\n7. Add a splash of water, cover and simmer 10 minutes.\n8. Garnish with cilantro and serve hot.",
                    Region = "Somali",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 25,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "meat stir fry cubed",
                    Tags = new List<string> { "Somali", "African", "Lamb", "Dinner", "Quick Meal" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Lamb or chicken", "500g, cubed"),
                        new("Onion", "1 large, sliced"),
                        new("Bell peppers", "2, sliced"),
                        new("Tomatoes", "2, diced"),
                        new("Garlic", "4 cloves, minced"),
                        new("Xawaash spice blend", "1 tablespoon"),
                        new("Cumin", "1 teaspoon"),
                        new("Coriander", "1/2 teaspoon"),
                        new("Vegetable oil", "3 tablespoons"),
                        new("Fresh cilantro", "for garnish"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste")
                    }
                },

                // ===== MORE RECIPES =====
                new RecipeTemplate
                {
                    Title = "Honey Garlic Chicken",
                    Description = "Sticky, sweet, and savory chicken thighs glazed with honey and garlic. An easy crowd-pleaser that's perfect for busy weeknights.",
                    Instructions = "1. Season chicken thighs with salt and pepper.\n2. Heat oil in a large pan over medium-high heat.\n3. Cook chicken skin-side down for 5-6 minutes until crispy.\n4. Flip and cook another 5 minutes.\n5. Mix honey, soy sauce, garlic, and ginger in a bowl.\n6. Pour sauce over chicken, reduce heat to medium.\n7. Simmer 5-7 minutes, basting frequently until glazed.\n8. Garnish with sesame seeds and green onions.",
                    Region = "Asian",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 20,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "honey garlic chicken",
                    Tags = new List<string> { "Asian", "Chicken", "Dinner", "Quick Meal", "Sweet and Savory" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Chicken thighs", "6 pieces"),
                        new("Honey", "1/3 cup"),
                        new("Soy sauce", "1/4 cup"),
                        new("Garlic", "6 cloves, minced"),
                        new("Ginger", "1 tablespoon, minced"),
                        new("Vegetable oil", "2 tablespoons"),
                        new("Sesame seeds", "for garnish"),
                        new("Green onions", "for garnish"),
                        new("Salt", "to taste"),
                        new("Black pepper", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Caprese Salad",
                    Description = "Simple Italian salad showcasing ripe tomatoes, fresh mozzarella, and fragrant basil. A celebration of summer's best ingredients.",
                    Instructions = "1. Slice tomatoes and mozzarella into 1/4 inch thick rounds.\n2. Arrange alternating slices on a serving platter.\n3. Tuck fresh basil leaves between slices.\n4. Drizzle generously with extra virgin olive oil.\n5. Add balsamic glaze if using.\n6. Season with flaky sea salt and black pepper.\n7. Let sit 5 minutes for flavors to meld.\n8. Serve at room temperature.",
                    Region = "Italian",
                    PrepTimeMinutes = 10,
                    CookTimeMinutes = 0,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "caprese salad tomato mozzarella",
                    Tags = new List<string> { "Italian", "Salad", "Vegetarian", "Appetizer", "Fresh", "Summer" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Ripe tomatoes", "3 large"),
                        new("Fresh mozzarella", "250g"),
                        new("Fresh basil", "1 bunch"),
                        new("Extra virgin olive oil", "3 tablespoons"),
                        new("Balsamic glaze", "optional"),
                        new("Sea salt", "to taste"),
                        new("Black pepper", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Banana Bread",
                    Description = "Moist, tender banana bread with a perfectly golden crust. The best way to use up overripe bananas for a delicious homemade treat.",
                    Instructions = "1. Preheat oven to 350°F (175°C). Grease a 9x5 loaf pan.\n2. Mash bananas in a large bowl.\n3. Mix in melted butter, sugar, egg, and vanilla.\n4. In another bowl, whisk flour, baking soda, and salt.\n5. Fold dry ingredients into wet until just combined.\n6. Add nuts or chocolate chips if using.\n7. Pour into prepared pan.\n8. Bake 55-60 minutes until a toothpick comes out clean.",
                    Region = "American",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 60,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "banana bread slice",
                    Tags = new List<string> { "American", "Breakfast", "Dessert", "Vegetarian", "Baking" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Ripe bananas", "3-4"),
                        new("All-purpose flour", "1.5 cups"),
                        new("Sugar", "3/4 cup"),
                        new("Butter", "1/3 cup, melted"),
                        new("Egg", "1 large"),
                        new("Vanilla extract", "1 teaspoon"),
                        new("Baking soda", "1 teaspoon"),
                        new("Salt", "1/4 teaspoon"),
                        new("Walnuts", "1/2 cup, chopped, optional")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Beef Kofta Kebabs",
                    Description = "Juicy, spiced ground beef kebabs grilled to perfection. These Middle Eastern favorites are perfect with rice, flatbread, and tahini.",
                    Instructions = "1. Combine ground beef with grated onion, garlic, and all spices.\n2. Add chopped parsley and mix well with hands.\n3. Cover and refrigerate for 30 minutes.\n4. Form mixture around metal skewers into long sausage shapes.\n5. Preheat grill or grill pan to high heat.\n6. Grill kofta for 4-5 minutes per side until cooked through.\n7. Rest for 2 minutes before serving.\n8. Serve with flatbread, tahini sauce, and fresh salad.",
                    Region = "Middle Eastern",
                    PrepTimeMinutes = 20,
                    CookTimeMinutes = 10,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "kofta kebab grilled",
                    Tags = new List<string> { "Middle Eastern", "Beef", "Dinner", "Grilled", "High Protein" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Ground beef", "500g"),
                        new("Onion", "1 small, grated"),
                        new("Garlic", "3 cloves, minced"),
                        new("Fresh parsley", "1/4 cup, chopped"),
                        new("Cumin", "1 teaspoon"),
                        new("Coriander", "1 teaspoon"),
                        new("Paprika", "1/2 teaspoon"),
                        new("Cinnamon", "1/4 teaspoon"),
                        new("Allspice", "1/4 teaspoon"),
                        new("Salt", "1 teaspoon"),
                        new("Black pepper", "1/2 teaspoon")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Mujaddara",
                    Description = "Humble but delicious lentils and rice topped with crispy fried onions. This Middle Eastern comfort food is simple, satisfying, and budget-friendly.",
                    Instructions = "1. Slice onions thin. Fry 2/3 in oil until deep golden and crispy. Set aside.\n2. In remaining oil, sauté remaining onion until soft.\n3. Add lentils and enough water to cover by 2 inches.\n4. Boil until lentils are almost tender, about 15 minutes.\n5. Add rice, cumin, salt, and more water if needed.\n6. Cover and cook on low until rice is done, about 18 minutes.\n7. Fluff with fork.\n8. Top generously with crispy onions. Serve with yogurt.",
                    Region = "Middle Eastern",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 40,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "lentils rice onions",
                    Tags = new List<string> { "Middle Eastern", "Lebanese", "Vegan", "Vegetarian", "Rice", "Lentils", "Budget" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Brown or green lentils", "1 cup"),
                        new("Long-grain rice", "1 cup"),
                        new("Onions", "3 large, sliced thin"),
                        new("Olive oil", "1/2 cup"),
                        new("Cumin", "1 teaspoon"),
                        new("Salt", "1.5 teaspoons"),
                        new("Black pepper", "1/2 teaspoon"),
                        new("Water", "4 cups"),
                        new("Plain yogurt", "for serving")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Fattoush Salad",
                    Description = "Vibrant Lebanese salad with crispy pita chips and tangy sumac dressing. Fresh, crunchy, and bursting with Middle Eastern flavors.",
                    Instructions = "1. Toast or fry pita pieces until crispy.\n2. Chop all vegetables into bite-sized pieces.\n3. Combine vegetables in a large bowl.\n4. Whisk olive oil, lemon juice, sumac, and garlic for dressing.\n5. Season dressing with salt and pepper.\n6. Toss salad with dressing just before serving.\n7. Add pita chips and toss gently.\n8. Serve immediately while pita is crispy.",
                    Region = "Lebanese",
                    PrepTimeMinutes = 20,
                    CookTimeMinutes = 5,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "fattoush lebanese salad",
                    Tags = new List<string> { "Middle Eastern", "Lebanese", "Salad", "Vegan", "Vegetarian", "Fresh" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Pita bread", "2 rounds"),
                        new("Romaine lettuce", "1 head, chopped"),
                        new("Cucumber", "1 large, diced"),
                        new("Tomatoes", "2, diced"),
                        new("Radishes", "5, sliced thin"),
                        new("Green onions", "4, sliced"),
                        new("Fresh mint", "1/4 cup, chopped"),
                        new("Fresh parsley", "1/4 cup, chopped"),
                        new("Sumac", "1 tablespoon"),
                        new("Olive oil", "1/4 cup"),
                        new("Lemon juice", "3 tablespoons"),
                        new("Garlic", "1 clove, minced"),
                        new("Salt", "to taste")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Chicken Fried Rice",
                    Description = "Classic takeout-style fried rice with tender chicken and vegetables. Better than delivery and ready in 20 minutes.",
                    Instructions = "1. Use day-old rice for best results.\n2. Season diced chicken with salt and soy sauce.\n3. Scramble eggs in hot wok, set aside.\n4. Cook chicken until done, set aside.\n5. Stir-fry vegetables for 2 minutes.\n6. Add cold rice, break up clumps.\n7. Add soy sauce, return chicken and eggs.\n8. Finish with sesame oil and green onions.",
                    Region = "Asian",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 15,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "chicken fried rice",
                    Tags = new List<string> { "Asian", "Chinese", "Chicken", "Rice", "Quick Meal", "Dinner" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Cooked rice", "4 cups, cold"),
                        new("Chicken breast", "200g, diced"),
                        new("Eggs", "2"),
                        new("Frozen peas and carrots", "1 cup"),
                        new("Green onions", "4, chopped"),
                        new("Soy sauce", "3 tablespoons"),
                        new("Sesame oil", "1 tablespoon"),
                        new("Vegetable oil", "3 tablespoons"),
                        new("Garlic", "2 cloves, minced"),
                        new("Salt", "to taste"),
                        new("White pepper", "1/4 teaspoon")
                    }
                },
                new RecipeTemplate
                {
                    Title = "Mediterranean Quinoa Bowl",
                    Description = "Healthy, colorful grain bowl with Mediterranean flavors. Packed with protein, fiber, and fresh vegetables for a nutritious meal.",
                    Instructions = "1. Cook quinoa according to package directions, let cool.\n2. Prepare all vegetables - dice cucumber, halve tomatoes, slice onion.\n3. Mix quinoa with vegetables.\n4. Add chickpeas and crumbled feta.\n5. Whisk olive oil, lemon juice, garlic, and oregano for dressing.\n6. Drizzle dressing over bowl and toss.\n7. Top with olives and fresh herbs.\n8. Season with salt and pepper to taste.",
                    Region = "Mediterranean",
                    PrepTimeMinutes = 15,
                    CookTimeMinutes = 15,
                    Difficulty = Difficulty.Easy,
                    PexelsSearchTerm = "mediterranean quinoa bowl",
                    Tags = new List<string> { "Mediterranean", "Healthy", "Vegetarian", "Grain Bowl", "Lunch", "Meal Prep" },
                    Ingredients = new List<IngredientTemplate>
                    {
                        new("Quinoa", "1 cup"),
                        new("Cucumber", "1, diced"),
                        new("Cherry tomatoes", "1 cup, halved"),
                        new("Red onion", "1/4, sliced"),
                        new("Chickpeas", "1 can, drained"),
                        new("Feta cheese", "1/2 cup, crumbled"),
                        new("Kalamata olives", "1/4 cup"),
                        new("Olive oil", "3 tablespoons"),
                        new("Lemon juice", "2 tablespoons"),
                        new("Dried oregano", "1 teaspoon"),
                        new("Garlic", "1 clove, minced"),
                        new("Fresh parsley", "2 tablespoons"),
                        new("Salt", "to taste")
                    }
                },
            };
        }

        #endregion

        #region Helper Classes

        private class RecipeTemplate
        {
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public string Instructions { get; set; } = "";
            public string? Region { get; set; }
            public int PrepTimeMinutes { get; set; }
            public int CookTimeMinutes { get; set; }
            public Difficulty Difficulty { get; set; }
            public string PexelsSearchTerm { get; set; } = "";
            public List<string> Tags { get; set; } = new();
            public List<IngredientTemplate> Ingredients { get; set; } = new();
        }

        private class IngredientTemplate
        {
            public string Name { get; set; }
            public string Quantity { get; set; }

            public IngredientTemplate(string name, string quantity)
            {
                Name = name;
                Quantity = quantity;
            }
        }

        private class PexelsImageResult
        {
            public string Url { get; set; } = "";
            public string Photographer { get; set; } = "";
            public string PhotographerUrl { get; set; } = "";
        }

        private class PexelsResponse
        {
            [JsonPropertyName("photos")]
            public List<PexelsPhoto>? Photos { get; set; }
        }

        private class PexelsPhoto
        {
            [JsonPropertyName("photographer")]
            public string? Photographer { get; set; }

            [JsonPropertyName("photographer_url")]
            public string? PhotographerUrl { get; set; }

            [JsonPropertyName("src")]
            public PexelsSrc? Src { get; set; }
        }

        private class PexelsSrc
        {
            [JsonPropertyName("original")]
            public string? Original { get; set; }

            [JsonPropertyName("large2x")]
            public string? Large2X { get; set; }

            [JsonPropertyName("large")]
            public string? Large { get; set; }
        }

        #endregion
    }
}

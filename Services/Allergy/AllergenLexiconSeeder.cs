using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Platerly.Api.Data;
using Platerly.Api.Models;

namespace Platerly.Api.Services.Allergy
{
    /// <summary>
    /// Seeds a comprehensive allergen keyword lexicon and links each keyword to its canonical Allergy.
    /// Safe to run multiple times (idempotent).
    /// </summary>
    public sealed class AllergyLexiconSeeder
    {
        private readonly PlaterlyDbContext _db;

        public AllergyLexiconSeeder(PlaterlyDbContext db) => _db = db;

        private sealed class Row
        {
            public AllergenKeywordKind Kind { get; init; }
            public string Text { get; init; } = "";
            public string? ENumber { get; init; }
            public string? Notes { get; init; }
        }

        /// <summary>
        /// Seeds the <c>AllergenKeywords</c> table and links each keyword to its canonical
        /// <c>Allergy</c> rows. Existing keywords are skipped by text comparison (case-insensitive).
        /// Keyword-to-allergy associations are upserted by ensuring the link row exists.
        /// Safe to run on every deploy (idempotent).
        /// </summary>
        public async Task SeedAsync(CancellationToken ct = default)
        {
            // ------------------------------------------------------------
            // Canonical allergy name -> keyword rows
            // COMPREHENSIVE ALLERGEN LEXICON
            // ------------------------------------------------------------
            var lexicon = new Dictionary<string, Row[]>(StringComparer.OrdinalIgnoreCase)
            {
                // ============================================================
                // PEANUTS (Arachis hypogaea)
                // ============================================================
                ["Peanuts"] = new[]
                {
                    // Primary terms
                    new Row { Kind = AllergenKeywordKind.Food, Text = "peanut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "peanuts" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "peanut butter" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "peanut oil" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "peanut flour" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "peanut protein" },

                    // Alternative names
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "groundnut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "groundnuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "ground nut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "goober" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "goober pea" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "monkey nut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "monkey nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "earth nut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "earthnut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "arachis" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "arachis oil" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "arachis hypogaea" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "arachide" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "beer nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "mixed nuts", Notes = "Often contains peanuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nut mix", Notes = "Often contains peanuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "mandelonas", Notes = "Peanuts soaked in almond flavoring" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nu-nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "valencias" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "hypogaea" },

                    // Products that often contain peanuts
                    new Row { Kind = AllergenKeywordKind.Food, Text = "satay", Notes = "Usually contains peanuts" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "satay sauce" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pad thai", Notes = "Often contains peanuts" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "kung pao", Notes = "Often contains peanuts" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "gado gado", Notes = "Contains peanut sauce" },
                },

                // ============================================================
                // TREE NUTS
                // ============================================================
                ["Tree Nuts"] = new[]
                {
                    // General
                    new Row { Kind = AllergenKeywordKind.Category, Text = "tree nut" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "tree nuts" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nut butter" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nut oil" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nut paste" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nut meal" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nut flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nut milk" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "mixed nuts" },

                    // ALMONDS
                    new Row { Kind = AllergenKeywordKind.Food, Text = "almond" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "almonds" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "almond butter" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "almond milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "almond oil" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "almond flour" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "almond meal" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "almond paste" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "almond extract" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "marzipan" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "amaretto", Notes = "Almond-flavored liqueur" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "frangipane" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "praline", Notes = "Often contains almonds" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "orgeat", Notes = "Almond syrup" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "amandine" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "blanched almonds" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "marcona" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "mandorla" },

                    // WALNUTS
                    new Row { Kind = AllergenKeywordKind.Food, Text = "walnut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "walnuts" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "walnut oil" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "english walnut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "persian walnut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "black walnut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "california walnut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "butternut", Notes = "White walnut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "heartnut" },

                    // CASHEWS
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cashew" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cashews" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cashew butter" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cashew milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cashew cream" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "anacardium" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "kaju" },

                    // PECANS
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pecan" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pecans" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pecan oil" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pecan pie" },
                    // HAZELNUTS / FILBERTS
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hazelnut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hazelnuts" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hazelnut oil" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hazelnut butter" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "filbert" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "filberts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cobnut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cobnuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "gianduja", Notes = "Chocolate-hazelnut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "gianduia" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nutella", Notes = "Contains hazelnuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "frangelico", Notes = "Hazelnut liqueur" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nocciola" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "avellana" },

                    // MACADAMIA
                    new Row { Kind = AllergenKeywordKind.Food, Text = "macadamia" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "macadamias" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "macadamia nut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "macadamia nuts" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "macadamia oil" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "queensland nut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "bush nut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "maroochi nut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "hawaii nut" },

                    // PISTACHIOS
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pistachio" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pistachios" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pistachio oil" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pistachio butter" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "pista" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "pistache" },

                    // BRAZIL NUTS
                    new Row { Kind = AllergenKeywordKind.Food, Text = "brazil nut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "brazil nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "para nut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "para nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cream nut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "castanha" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "castana" },

                    // PINE NUTS
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pine nut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pine nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "pignoli" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "pignolia" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "pigñon" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "pinon" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "pinyon" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "pesto", Notes = "Usually contains pine nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "indian nut" },

                    // CHESTNUTS
                    new Row { Kind = AllergenKeywordKind.Food, Text = "chestnut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "chestnuts" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "chestnut flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "marron" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "marrons" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "castagne" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "chinquapin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "water chestnut", Notes = "Not a true nut but may cross-react" },

                    // OTHER TREE NUTS
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hickory nut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "beechnut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "beech nut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "ginkgo nut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lychee nut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "shea nut" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nougat", Notes = "Often contains nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "torrone", Notes = "Often contains nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "turron", Notes = "Often contains nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "baklava", Notes = "Contains nuts" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "dukkah", Notes = "Nut and spice blend" },
                },

                // ============================================================
                // MILK / DAIRY
                // ============================================================
                ["Milk"] = new[]
                {
                    // Primary terms
                    new Row { Kind = AllergenKeywordKind.Food, Text = "milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "whole milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "skim milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "skimmed milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "low fat milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "2% milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "1% milk" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "dairy" },

                    // Milk proteins
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "casein" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "caseinate" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sodium caseinate" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "calcium caseinate" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "casein hydrolysate" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "whey" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "whey protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "whey powder" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "lactalbumin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "lactoglobulin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "beta-lactoglobulin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "lactoferrin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "milk protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "milk protein isolate" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "milk protein concentrate" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "recaldent", Notes = "Contains casein" },

                    // Milk sugar
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "lactose" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "lactulose" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "milk sugar" },

                    // Milk forms
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "milk powder" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "powdered milk" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "dry milk" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "dried milk" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "milk solids" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nonfat milk solids" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "condensed milk" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "evaporated milk" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "malted milk" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "milk fat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "anhydrous milk fat" },

                    // Butter
                    new Row { Kind = AllergenKeywordKind.Food, Text = "butter" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "buttermilk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "butterfat" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "butter oil" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "butter solids" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "clarified butter" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "brown butter" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "ghee" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "beurre" },

                    // Cream
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cream" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "heavy cream" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "heavy whipping cream" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "whipping cream" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "light cream" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "half and half" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "half-and-half" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sour cream" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "creme fraiche" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "crème fraîche" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "clotted cream" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "double cream" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "single cream" },

                    // Cheese
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cheese" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cheddar" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mozzarella" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "parmesan" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "parmigiano" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pecorino" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "brie" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "camembert" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "gouda" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "gruyere" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "swiss cheese" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "emmental" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "feta" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "blue cheese" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "gorgonzola" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "roquefort" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "stilton" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cream cheese" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cottage cheese" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "ricotta" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mascarpone" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "provolone" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "monterey jack" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "colby" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "havarti" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fontina" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "asiago" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "manchego" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "queso" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "paneer" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "halloumi" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "burrata" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fromage" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "curds" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cheese powder" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cheese flavor" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cheese flavoring" },

                    // Yogurt
                    new Row { Kind = AllergenKeywordKind.Food, Text = "yogurt" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "yoghurt" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "greek yogurt" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "kefir" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lassi" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "ayran" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "skyr" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "labneh" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "labne" },

                    // Ice cream and frozen
                    new Row { Kind = AllergenKeywordKind.Food, Text = "ice cream" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "gelato" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "frozen yogurt" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "frozen custard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sherbet", Notes = "Often contains milk" },

                    // Other dairy products
                    new Row { Kind = AllergenKeywordKind.Food, Text = "custard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pudding" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dulce de leche" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "lacto" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "lactic" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "galactose" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "nisin", Notes = "Dairy-derived preservative" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "rennet", Notes = "Used in cheese making" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "simplesse", Notes = "May contain milk protein" },

                    // Animal milks (all contain similar proteins)
                    new Row { Kind = AllergenKeywordKind.Food, Text = "goat milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "goat cheese" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sheep milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sheep cheese" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "buffalo milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "buffalo mozzarella" },
                },

                // ============================================================
                // EGGS
                // ============================================================
                ["Eggs"] = new[]
                {
                    // Primary terms
                    new Row { Kind = AllergenKeywordKind.Food, Text = "egg" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "eggs" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "egg white" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "egg whites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "egg yolk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "egg yolks" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "whole egg" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dried egg" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "powdered egg" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "egg powder" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "egg solids" },

                    // Egg proteins
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "albumin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "albumen" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "ovalbumin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "ovomucin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "ovomucoid" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "ovovitellin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "ovoglobulin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "globulin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "livetin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "lysozyme", Notes = "Egg-derived enzyme" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "vitellin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "phosvitin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "apovitellin" },

                    // Egg-containing products
                    new Row { Kind = AllergenKeywordKind.Food, Text = "meringue" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mayonnaise" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mayo" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "aioli" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hollandaise" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bearnaise" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "béarnaise" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "eggnog" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "egg nog" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "custard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "quiche" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "frittata" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "omelette" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "omelet" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "souffle" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soufflé" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pavlova" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "marshmallow", Notes = "May contain egg whites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "nougat", Notes = "Often contains egg whites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "marzipan", Notes = "May contain egg" },

                    // Other forms
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "egg lecithin", Notes = "Egg-derived" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "egg wash" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "egg glaze" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "egg substitute", Notes = "May still contain egg" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "fat replacer", Notes = "May contain egg" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "surimi", Notes = "May contain egg" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "simplesse", Notes = "May contain egg" },

                    // French terms
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "oeuf" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "oeufs" },

                    // Other egg types
                    new Row { Kind = AllergenKeywordKind.Food, Text = "duck egg" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "quail egg" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "goose egg" },
                },

                // ============================================================
                // WHEAT / GLUTEN
                // ============================================================
                ["Gluten"] = new[]
                {
                    // Primary grains
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "gluten" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "wheat" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "barley" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "rye" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "triticale" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "spelt" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "kamut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "einkorn" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "emmer" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "farro" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "durum" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "semolina" },

                    // Wheat forms and derivatives
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "wheat flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "whole wheat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "wholewheat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "wheat germ" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "wheat bran" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "wheat starch" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "wheat protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "hydrolyzed wheat protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "wheat gluten" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "vital wheat gluten" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "modified wheat starch" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "wheat grass", Notes = "May cross-react" },

                    // Flour types
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "all-purpose flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "all purpose flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "bread flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cake flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "pastry flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "self-rising flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "self rising flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "enriched flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "bleached flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "unbleached flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "graham flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "atta" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "maida" },

                    // Wheat products
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bulgur" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "couscous" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "farina" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "matzo" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "matzoh" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "matza" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "matzah" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "orzo", Notes = "Wheat pasta" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "seitan" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fu", Notes = "Dried wheat gluten" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "malt" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "malt extract" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "malt syrup" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "malt flavoring" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "malt vinegar" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "malted barley" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "malted milk" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "brewer's yeast", Notes = "May contain gluten" },

                    // Pasta and noodles
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pasta" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "spaghetti" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "macaroni" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "penne" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fettuccine" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "linguine" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lasagna" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "ravioli" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tortellini" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "gnocchi", Notes = "May contain wheat" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "udon" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "ramen", Notes = "Wheat noodles" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "egg noodles" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "noodles" },

                    // Bread and baked goods
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bread" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "breadcrumbs" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bread crumbs" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "panko" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "croutons" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pita" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "naan" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tortilla", Notes = "Flour tortillas contain wheat" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "flour tortilla" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "croissant" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bagel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "baguette" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "brioche" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "challah" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "focaccia" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "ciabatta" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sourdough" },

                    // Cereals
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cereal" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bran" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cream of wheat" },

                    // Other wheat-containing items
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "hydrolyzed vegetable protein", Notes = "May contain wheat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "hvp", Notes = "May contain wheat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "textured vegetable protein", Notes = "May contain wheat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "tvp", Notes = "May contain wheat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "modified food starch", Notes = "May contain wheat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "starch", Notes = "May be wheat-derived" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "dextrin", Notes = "May be wheat-derived" },

                    // Sauces with gluten
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy sauce", Notes = "Usually contains wheat" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "teriyaki", Notes = "Contains soy sauce (wheat)" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hoisin", Notes = "May contain wheat" },

                    // Beer and alcohol
                    new Row { Kind = AllergenKeywordKind.Food, Text = "beer", Notes = "Contains gluten" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "ale", Notes = "Contains gluten" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lager", Notes = "Contains gluten" },
                },

                // ============================================================
                // SOY / SOYA
                // ============================================================
                ["Soy"] = new[]
                {
                    // Primary terms
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soya" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soybean" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soybeans" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soya bean" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soya beans" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "edamame" },

                    // Soy products
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tofu" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tempeh" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "natto" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "miso" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "miso paste" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "miso soup" },

                    // Soy sauces
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy sauce" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "shoyu" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tamari" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "teriyaki" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "ponzu", Notes = "Often contains soy" },

                    // Soy derivatives
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy lecithin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soya lecithin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "lecithin", Notes = "Often soy-derived" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy protein isolate" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy protein concentrate" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "hydrolyzed soy protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soya flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy fiber" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy albumin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy isoflavones" },

                    // Soy oils
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soybean oil" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy oil" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "vegetable oil", Notes = "Often soy-based" },

                    // Soy milk and alternatives
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soymilk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soya milk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy yogurt" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy cheese" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy ice cream" },

                    // Textured soy
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "textured vegetable protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "tvp" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "textured soy protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "tsp" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy meat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soy burger" },

                    // Other soy products
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy nut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy nuts" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy sprouts" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "yuba", Notes = "Tofu skin" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "okara", Notes = "Soy pulp" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "kinako", Notes = "Roasted soy flour" },

                    // May contain soy
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "hydrolyzed vegetable protein", Notes = "May contain soy" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "hvp", Notes = "May contain soy" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "natural flavoring", Notes = "May contain soy" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "vegetable broth", Notes = "May contain soy" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "vegetable starch", Notes = "May contain soy" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "vegetable gum", Notes = "May contain soy" },
                },

                // ============================================================
                // FISH
                // ============================================================
                ["Fish"] = new[]
                {
                    // General terms
                    new Row { Kind = AllergenKeywordKind.Category, Text = "fish" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "seafood" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "finfish" },

                    // Common fish species
                    new Row { Kind = AllergenKeywordKind.Food, Text = "salmon" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tuna" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cod" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tilapia" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "halibut" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bass" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sea bass" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "striped bass" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "trout" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "rainbow trout" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mackerel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sardine" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sardines" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "anchovy" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "anchovies" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "haddock" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "herring" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "perch" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "snapper" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "red snapper" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sole" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "swordfish" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "catfish" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "flounder" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "grouper" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mahi mahi" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mahi-mahi" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pollock" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pike" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "walleye" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "whiting" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "yellowtail" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "monkfish" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "orange roughy" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pompano" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "rockfish" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "eel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "unagi" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "branzino" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "turbot" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "skate" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "carp" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bream" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dorado" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "barramundi" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bluefish" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bonito" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "butterfish" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cobia" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "croaker" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dory" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "john dory" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "escolar" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hake" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "kingfish" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lingcod" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "marlin" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mullet" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "opah" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sablefish" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "shad" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "shark" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "smelt" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sturgeon" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tilefish" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "wahoo" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "amberjack" },

                    // Fish products
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fish sauce" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fish stock" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fish broth" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fish paste" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fish oil" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fish gelatin" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "caviar" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "roe" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "fish roe" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "ikura" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tobiko" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "masago" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "surimi" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "imitation crab", Notes = "Usually made from fish" },

                    // Asian fish products
                    new Row { Kind = AllergenKeywordKind.Food, Text = "nam pla", Notes = "Thai fish sauce" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "nuoc mam", Notes = "Vietnamese fish sauce" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "patis", Notes = "Filipino fish sauce" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bagoong", Notes = "Filipino fish paste" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dashi", Notes = "May contain fish" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bonito flakes" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "katsuobushi" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "furikake", Notes = "Often contains fish" },

                    // Sauces that contain fish
                    new Row { Kind = AllergenKeywordKind.Food, Text = "worcestershire", Notes = "Contains anchovies" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "worcestershire sauce" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "caesar dressing", Notes = "Usually contains anchovies" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "caesar salad", Notes = "Dressing contains anchovies" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "puttanesca", Notes = "Contains anchovies" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bagna cauda", Notes = "Contains anchovies" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "gentleman's relish", Notes = "Anchovy paste" },

                    // Omega-3 supplements
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "omega-3", Notes = "Often fish-derived" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "omega 3", Notes = "Often fish-derived" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "fish oil capsules" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cod liver oil" },

                    // Isinglass
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "isinglass", Notes = "Fish bladder, used in beer/wine" },
                },

                // ============================================================
                // SHELLFISH (Crustaceans and Mollusks)
                // ============================================================
                ["Shellfish"] = new[]
                {
                    // General terms
                    new Row { Kind = AllergenKeywordKind.Category, Text = "shellfish" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "crustacean" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "crustaceans" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "mollusk" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "mollusks" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "mollusc" },
                    new Row { Kind = AllergenKeywordKind.Category, Text = "molluscs" },

                    // CRUSTACEANS
                    // Shrimp
                    new Row { Kind = AllergenKeywordKind.Food, Text = "shrimp" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "prawns" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "prawn" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "scampi" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "crevette" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "shrimp paste" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "shrimp sauce" },

                    // Lobster
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lobster" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "langouste" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "langoustine" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "langostino" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "rock lobster" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "spiny lobster" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "crayfish" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "crawfish" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "crawdad" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "crawdads" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "écrevisse" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "yabby" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "yabbies" },

                    // Crab
                    new Row { Kind = AllergenKeywordKind.Food, Text = "crab" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "crabs" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "crabmeat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "crab meat" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "king crab" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "snow crab" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "dungeness crab" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "blue crab" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "soft shell crab" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "stone crab" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "kani" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "imitation crab", Notes = "May trigger shellfish allergy" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "crab stick" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "crab sticks" },

                    // MOLLUSKS
                    // Clams
                    new Row { Kind = AllergenKeywordKind.Food, Text = "clam" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "clams" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "littleneck" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cherrystone" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "quahog" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "geoduck" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cockle" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cockles" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "clam juice" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "clam chowder", Notes = "Contains clams" },

                    // Mussels
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mussel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mussels" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "moules" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "green lip mussel" },

                    // Oysters
                    new Row { Kind = AllergenKeywordKind.Food, Text = "oyster" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "oysters" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "oyster sauce" },

                    // Scallops
                    new Row { Kind = AllergenKeywordKind.Food, Text = "scallop" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "scallops" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "bay scallop" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sea scallop" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "coquille" },

                    // Squid/Calamari
                    new Row { Kind = AllergenKeywordKind.Food, Text = "squid" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "calamari" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "squid ink" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "cuttlefish" },

                    // Octopus
                    new Row { Kind = AllergenKeywordKind.Food, Text = "octopus" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "tako" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "pulpo" },

                    // Snails
                    new Row { Kind = AllergenKeywordKind.Food, Text = "snail" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "snails" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "escargot" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "escargots" },

                    // Abalone
                    new Row { Kind = AllergenKeywordKind.Food, Text = "abalone" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "paua" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "ormer" },

                    // Other shellfish
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sea urchin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "uni" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "whelk" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "periwinkle" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "limpet" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "conch" },

                    // Shellfish extracts
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "glucosamine", Notes = "Often shellfish-derived" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "chitosan", Notes = "Shellfish-derived" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "chitin", Notes = "Shellfish-derived" },

                    // Asian preparations
                    new Row { Kind = AllergenKeywordKind.Food, Text = "shrimp paste" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "belacan" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "terasi" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "kapi" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "xo sauce", Notes = "Contains dried shrimp" },
                },

                // ============================================================
                // SESAME
                // ============================================================
                ["Sesame"] = new[]
                {
                    // Primary terms
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sesame" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sesame seeds" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sesame seed" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sesame oil" },

                    // Products containing sesame
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tahini" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tahina" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tehina" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hummus", Notes = "Usually contains tahini" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "houmous" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "humous" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "halvah" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "halva" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "halawa" },

                    // Alternative names
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "benne" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "benne seeds" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "gingelly" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "gingelly oil" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "til" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sim sim" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sesamol" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sesamolin" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sesamum indicum" },

                    // Sesame flour and paste
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sesame flour" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sesame paste" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sesame butter" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sesame salt" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "gomasio" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "gomashio" },

                    // Asian sesame products
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "goma", Notes = "Japanese for sesame" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "zhima", Notes = "Chinese for sesame" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "chamkkae", Notes = "Korean for sesame" },

                    // Foods often containing sesame
                    new Row { Kind = AllergenKeywordKind.Food, Text = "falafel", Notes = "Often served with tahini" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "baba ganoush", Notes = "Usually contains tahini" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "baba ghanoush" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dukkah", Notes = "Contains sesame" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "za'atar", Notes = "Contains sesame" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "zaatar" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bagel", Notes = "Often has sesame seeds" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "burger bun", Notes = "Often has sesame seeds" },

                    // Vegetable oils that may be sesame
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "vegetable oil", Notes = "May contain sesame" },
                },

                // ============================================================
                // NICKEL (High-nickel foods)
                // ============================================================
                ["Nickel"] = new[]
                {
                    // Chocolate/Cocoa
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cocoa", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cacao", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "chocolate", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dark chocolate", Notes = "Very high nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "milk chocolate", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cocoa powder", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "chocolate chips", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hot chocolate", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "carob", Notes = "High nickel" },

                    // Legumes
                    new Row { Kind = AllergenKeywordKind.Category, Text = "legumes", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "beans", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "black beans", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "kidney beans", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "navy beans", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pinto beans", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lima beans", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cannellini beans", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "chickpeas", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "garbanzo beans", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lentils", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "red lentils", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "green lentils", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "peas", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "split peas", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "green peas", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soybeans", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tofu", Notes = "High nickel (from soy)" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tempeh", Notes = "High nickel (from soy)" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "edamame", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hummus", Notes = "High nickel (from chickpeas)" },

                    // Nuts and seeds
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cashews", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cashew", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "almonds", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "peanuts", Notes = "Moderate-High nickel (high variability)" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "hazelnuts", Notes = "Moderate nickel" },
                    // Walnuts: Rebelytics v9.1.1 confirms 100 μg or more per serving = HIGH.
                    new Row { Kind = AllergenKeywordKind.Food, Text = "walnuts", Notes = "High nickel — 100 μg or more per serving" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pistachios", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sunflower seeds", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "flax seeds", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "flaxseed", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "chia seeds", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sesame seeds", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pumpkin seeds", Notes = "High nickel" },

                    // Grains
                    new Row { Kind = AllergenKeywordKind.Food, Text = "oats", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "oatmeal", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "whole wheat", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "whole grain", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "buckwheat", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "millet", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "quinoa", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "wheat germ", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "bran", Notes = "High nickel" },

                    // Vegetables
                    // Spinach: Data shows mean 8.94 μg/serving = LOW, but SD is 9.99 (very high).
                    // Some samples reached 51 μg. Prior "High nickel" label was inaccurate.
                    // Source: Rebelytics R&D Inc. v9.1.1 (2025), 45 data points.
                    new Row { Kind = AllergenKeywordKind.Food, Text = "spinach", Notes = "Low-Moderate nickel (high variability — some samples HIGH)" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "kale", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "asparagus", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "broccoli", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "brussels sprouts", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cauliflower", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "corn", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lettuce", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mushrooms", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "onions", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tomatoes", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "potatoes", Notes = "Moderate nickel" },

                    // Canned foods
                    new Row { Kind = AllergenKeywordKind.Category, Text = "canned", Notes = "Nickel leaches from cans" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "canned food", Notes = "Nickel leaches from cans" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "canned vegetables", Notes = "Nickel leaches from cans" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "canned beans", Notes = "Double nickel concern" },

                    // Beverages
                    new Row { Kind = AllergenKeywordKind.Food, Text = "tea", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "black tea", Notes = "Higher nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "green tea", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "coffee", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "beer", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "red wine", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "soy milk", Notes = "High nickel (from soy)" },

                    // Dried fruits
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dried fruit", Notes = "Often high nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dried apricots", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dried figs", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "prunes", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "raisins", Notes = "Moderate nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dates", Notes = "Moderate nickel" },

                    // Shellfish (also high in nickel)
                    new Row { Kind = AllergenKeywordKind.Food, Text = "oysters", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mussels", Notes = "High nickel" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "clams", Notes = "Moderate nickel" },
                },

                // ============================================================
                // SULFITES (E220–E228 + common synonyms)
                // ============================================================
                ["Sulfites"] = new[]
                {
                    // E-numbers
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E220", ENumber = "E220", Notes = "Sulfur dioxide" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E221", ENumber = "E221", Notes = "Sodium sulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E222", ENumber = "E222", Notes = "Sodium bisulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E223", ENumber = "E223", Notes = "Sodium metabisulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E224", ENumber = "E224", Notes = "Potassium metabisulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E226", ENumber = "E226", Notes = "Calcium sulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E227", ENumber = "E227", Notes = "Calcium hydrogen sulfite" },
                    new Row { Kind = AllergenKeywordKind.ENumber, Text = "E228", ENumber = "E228", Notes = "Potassium hydrogen sulfite" },

                    // Chemical names
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sulfite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sulfites" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sulphite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sulphites" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sulfur dioxide" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sulphur dioxide" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sodium sulfite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sodium bisulfite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sodium metabisulfite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sodium metabisulphite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "potassium bisulfite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "potassium metabisulfite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "potassium metabisulphite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "calcium sulfite" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "calcium bisulfite" },

                    // Foods commonly containing sulfites
                    new Row { Kind = AllergenKeywordKind.Food, Text = "wine", Notes = "Contains sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "red wine", Notes = "Contains sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "white wine", Notes = "Contains sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "champagne", Notes = "Contains sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "beer", Notes = "May contain sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "cider", Notes = "May contain sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dried fruit", Notes = "Often contains sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dried apricots", Notes = "Usually contains sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "raisins", Notes = "May contain sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "grape juice", Notes = "May contain sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "molasses", Notes = "May contain sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "corn syrup", Notes = "May contain sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "vinegar", Notes = "May contain sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "pickles", Notes = "May contain sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "sauerkraut", Notes = "May contain sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "shrimp", Notes = "Often treated with sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "potatoes", Notes = "Pre-cut may have sulfites" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "guacamole", Notes = "Commercial may have sulfites" },
                },

                // ============================================================
                // MUSTARD
                // ============================================================
                ["Mustard"] = new[]
                {
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mustard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mustard seed" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mustard seeds" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mustard powder" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mustard flour" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mustard oil" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "yellow mustard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dijon mustard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "dijon" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "honey mustard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "whole grain mustard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "english mustard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "french mustard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "german mustard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "brown mustard" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "spicy mustard" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "sinapis" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "brassica" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "mustard greens" },
                },

                // ============================================================
                // CELERY
                // ============================================================
                ["Celery"] = new[]
                {
                    new Row { Kind = AllergenKeywordKind.Food, Text = "celery" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "celeriac" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "celery root" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "celery salt" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "celery seed" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "celery seeds" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "celery powder" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "celery leaf" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "celery leaves" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "apium graveolens" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lovage", Notes = "Related to celery" },
                },

                // ============================================================
                // LUPIN
                // ============================================================
                ["Lupin"] = new[]
                {
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lupin" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lupine" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lupini" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lupini beans" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lupin beans" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lupin flour" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lupin seed" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lupin seeds" },
                    new Row { Kind = AllergenKeywordKind.Food, Text = "lupin protein" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "lupinus" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "altramuz" },
                    new Row { Kind = AllergenKeywordKind.Synonym, Text = "tremocos" },
                },
            };

            // ------------------------------------------------------------
            // Load Allergy ids by name (Guid)
            // ------------------------------------------------------------
            var allergies = await _db.Allergies
                .AsNoTracking()
                .Select(a => new { a.Id, a.Name })
                .ToListAsync(ct);

            var allergyIdByName = allergies.ToDictionary(a => a.Name, a => a.Id, StringComparer.OrdinalIgnoreCase);

            // Preload all existing keywords once to reduce roundtrips
            var existingKeywords = await _db.AllergenKeywords
                .AsNoTracking()
                .Select(k => new { k.Id, k.Text, k.Kind })
                .ToListAsync(ct);

            // Case-insensitive lookup using normalized (lower) text:
            // (Kind, lowerText) -> keywordId
            var keywordIdByKey = new Dictionary<(AllergenKeywordKind Kind, string LowerText), int>();
            foreach (var k in existingKeywords)
            {
                var lower = k.Text?.ToLowerInvariant() ?? string.Empty;
                var key = (k.Kind, lower);
                if (!keywordIdByKey.ContainsKey(key))
                    keywordIdByKey[key] = k.Id;
            }

            // We'll collect new entities and save in batches
            var toInsertKeywords = new List<AllergenKeyword>();
            var toInsertLinks = new List<AllergenKeywordAllergy>();
            // Track keywords already queued for insertion to avoid duplicates
            var queuedKeywords = new HashSet<(AllergenKeywordKind Kind, string LowerText)>();

            foreach (var kvp in lexicon)
            {
                var allergyName = kvp.Key;
                var rows = kvp.Value;

                if (!allergyIdByName.TryGetValue(allergyName, out Guid allergyId))
                    continue; // skip if the canonical Allergy record is not present

                // Ensure keywords exist
                foreach (var r in rows)
                {
                    var key = (r.Kind, (r.Text ?? string.Empty).ToLowerInvariant());

                    // Skip if already exists in DB or already queued for insertion
                    if (keywordIdByKey.ContainsKey(key) || queuedKeywords.Contains(key))
                        continue;

                    // create new keyword
                    var k = new AllergenKeyword
                    {
                        Text = r.Text,
                        Kind = r.Kind,
                        ENumber = r.ENumber,
                        Notes = r.Notes
                    };
                    toInsertKeywords.Add(k);
                    queuedKeywords.Add(key);
                }

                // Flush new keywords for this allergy bucket so we have their IDs
                if (toInsertKeywords.Count > 0)
                {
                    _db.AllergenKeywords.AddRange(toInsertKeywords);
                    await _db.SaveChangesAsync(ct);

                    // refresh cache with newly created ids
                    foreach (var k in toInsertKeywords)
                    {
                        var lower = k.Text?.ToLowerInvariant() ?? string.Empty;
                        keywordIdByKey[(k.Kind, lower)] = k.Id;
                    }

                    toInsertKeywords.Clear();
                    queuedKeywords.Clear(); // Clear since they're now in keywordIdByKey
                }

                // Now ensure link rows exist
                foreach (var r in rows)
                {
                    var key = (r.Kind, (r.Text ?? string.Empty).ToLowerInvariant());
                    if (!keywordIdByKey.TryGetValue(key, out int kwId))
                        continue;

                    bool linkExists = await _db.AllergenKeywordAllergies
                        .AsNoTracking()
                        .AnyAsync(x => x.AllergenKeywordId == kwId && x.AllergyId == allergyId, ct);

                    if (!linkExists)
                    {
                        toInsertLinks.Add(new AllergenKeywordAllergy
                        {
                            AllergenKeywordId = kwId,
                            AllergyId = allergyId
                        });
                    }
                }
            }

            if (toInsertLinks.Count > 0)
            {
                _db.AllergenKeywordAllergies.AddRange(toInsertLinks);
                await _db.SaveChangesAsync(ct);
            }
        }
    }
}

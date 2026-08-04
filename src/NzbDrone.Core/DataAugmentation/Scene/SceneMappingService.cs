using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.DataAugmentation.Scene
{
    public interface ISceneMappingService
    {
        List<string> GetSceneNames(int tvdbId, List<int> seasonNumbers, List<int> sceneSeasonNumbers);
        int? FindTvdbId(string sceneTitle, string releaseTitle, int sceneSeasonNumber);
        List<SceneMapping> FindByTvdbId(int tvdbId);
        SceneMapping FindSceneMapping(string sceneTitle, string releaseTitle, int sceneSeasonNumber);
        int? GetSceneSeasonNumber(string seriesTitle, string releaseTitle);
        List<SceneMapping> UpsertUserMappings(List<SceneMapping> mappings, Series series);
    }

    public class SceneMappingService : ISceneMappingService,
                                       IHandle<SeriesRefreshStartingEvent>,
                                       IHandle<SeriesAddedEvent>,
                                       IHandle<SeriesImportedEvent>,
                                       IExecute<UpdateSceneMappingCommand>
    {
        // Must never equal an ISceneMappingProvider type name: UpdateMappings clears per
        // provider type, which is what makes user rows survive mapping updates.
        public const string UserMappingType = "User";

        private static readonly Regex SpaceSeparatorRegex = new (@"\p{Zs}", RegexOptions.Compiled);
        private static readonly Regex DashPunctuationRegex = new (@"\p{Pd}", RegexOptions.Compiled);
        private static readonly Regex QuotePunctuationRegex = new (@"[\p{Pi}\p{Pf}]", RegexOptions.Compiled);

        private readonly ISceneMappingRepository _repository;
        private readonly IEnumerable<ISceneMappingProvider> _sceneMappingProviders;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly ICachedDictionary<List<SceneMapping>> _getTvdbIdCache;
        private readonly ICachedDictionary<List<SceneMapping>> _findByTvdbIdCache;
        private bool _updatedAfterStartup;

        public SceneMappingService(ISceneMappingRepository repository,
                                   ICacheManager cacheManager,
                                   IEnumerable<ISceneMappingProvider> sceneMappingProviders,
                                   IEventAggregator eventAggregator,
                                   Logger logger)
        {
            _repository = repository;
            _sceneMappingProviders = sceneMappingProviders;
            _eventAggregator = eventAggregator;
            _logger = logger;

            _getTvdbIdCache = cacheManager.GetCacheDictionary<List<SceneMapping>>(GetType(), "tvdb_id");
            _findByTvdbIdCache = cacheManager.GetCacheDictionary<List<SceneMapping>>(GetType(), "find_tvdb_id");
        }

        public List<string> GetSceneNames(int tvdbId, List<int> seasonNumbers, List<int> sceneSeasonNumbers)
        {
            var mappings = FindByTvdbId(tvdbId);

            if (mappings == null)
            {
                return new List<string>();
            }

            var names = mappings.Where(n => seasonNumbers.Contains(n.SeasonNumber ?? -1) ||
                                            sceneSeasonNumbers.Contains(n.SceneSeasonNumber ?? -1) ||
                                            ((n.SeasonNumber ?? -1) == -1 && (n.SceneSeasonNumber ?? -1) == -1 && n.SceneOrigin != "tvdb"))
                                .Where(n => IsEnglish(n.SearchTerm))
                                .Select(n => n.SearchTerm)
                                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                                .ToList();

            return names;
        }

        public int? FindTvdbId(string seriesTitle, string releaseTitle, int sceneSeasonNumber)
        {
            return FindSceneMapping(seriesTitle, releaseTitle, sceneSeasonNumber)?.TvdbId;
        }

        public List<SceneMapping> FindByTvdbId(int tvdbId)
        {
            if (_findByTvdbIdCache.Count == 0)
            {
                RefreshCache();
            }

            var mappings = _findByTvdbIdCache.Find(tvdbId.ToString());

            if (mappings == null)
            {
                return new List<SceneMapping>();
            }

            return mappings;
        }

        public SceneMapping FindSceneMapping(string seriesTitle, string releaseTitle, int sceneSeasonNumber)
        {
            if (seriesTitle.IsNullOrWhiteSpace())
            {
                return null;
            }

            var mappings = FindMappings(seriesTitle, releaseTitle);

            if (mappings == null)
            {
                return null;
            }

            mappings = FilterSceneMappings(mappings, sceneSeasonNumber);

            var distinctMappings = mappings.DistinctBy(v => v.TvdbId).ToList();

            if (distinctMappings.Count == 0)
            {
                return null;
            }

            if (distinctMappings.Count == 1)
            {
                var mapping = distinctMappings.First();
                _logger.Debug("Found scene mapping for: {0}. TVDB ID for mapping: {1}", seriesTitle, mapping.TvdbId);
                return distinctMappings.First();
            }

            throw new InvalidSceneMappingException(mappings, releaseTitle);
        }

        public int? GetSceneSeasonNumber(string seriesTitle, string releaseTitle)
        {
            return FindSceneMapping(seriesTitle, releaseTitle, -1)?.SceneSeasonNumber;
        }

        public List<SceneMapping> UpsertUserMappings(List<SceneMapping> mappings, Series series)
        {
            var allMappings = _repository.All().ToList();
            var addList = new List<SceneMapping>();

            foreach (var mapping in mappings)
            {
                if (mapping.Title.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var searchTerm = NormalizeSearchTerm(mapping.Title);

                // A term GetSceneNames would discard yields a mapping that silently never
                // contributes a query; report it instead of storing a dead row.
                if (!IsEnglish(searchTerm))
                {
                    _logger.Warn("Skipping user scene mapping '{0}' for {1}: title contains characters that cannot be used in a search query", mapping.Title, series.Title);
                    continue;
                }

                var parseTerms = GetParseTerms(mapping.Title);

                if (!parseTerms.Any() || parseTerms.Contains(series.CleanTitle))
                {
                    continue;
                }

                // Mapped to a different series: inserting would make FindSceneMapping throw
                // InvalidSceneMappingException for every release with this title. Every spelling
                // is checked before any is inserted, so a title is taken whole or not at all.
                var conflict = allMappings.Concat(addList).FirstOrDefault(m => parseTerms.Contains(m.ParseTerm) && m.TvdbId != series.TvdbId);

                if (conflict != null)
                {
                    _logger.Warn("Skipping user scene mapping '{0}' for {1}: parse term already maps to tvdbid {2}", mapping.Title, series.Title, conflict.TvdbId);
                    continue;
                }

                // Already mapped for this series (any provider): idempotent skip.
                var newTerms = parseTerms
                    .Where(p => !allMappings.Any(m => m.ParseTerm == p && m.TvdbId == series.TvdbId))
                    .Where(p => !addList.Any(m => m.ParseTerm == p))
                    .ToList();

                foreach (var parseTerm in newTerms)
                {
                    addList.Add(new SceneMapping
                    {
                        Title = mapping.Title,
                        Comment = mapping.Comment,
                        TvdbId = series.TvdbId,
                        Type = UserMappingType,
                        SearchTerm = searchTerm,
                        ParseTerm = parseTerm,

                        // No season constraints and no "tvdb" origin, so GetSceneNames includes
                        // the mapping for every season search.
                        SeasonNumber = null,
                        SceneSeasonNumber = null,
                        SceneOrigin = null,
                        FilterRegex = null
                    });
                }
            }

            if (addList.Any())
            {
                _repository.InsertMany(addList);
                RefreshCache();
                _eventAggregator.PublishEvent(new SceneMappingsUpdatedEvent());
            }

            var addedTitles = addList.Select(m => m.Title).Distinct().Count();

            _logger.Debug("Upserted user scene mappings for {0}; Adding {1} titles ({2} rows), Skipping {3}.", series.Title, addedTitles, addList.Count, mappings.Count - addedTitles);

            return addList;
        }

        /// <summary>
        /// Parse terms a user title should be matched by. Follows upstream's convention that
        /// ParseTerm derives from Title, and adds the folded spelling when it differs: we search
        /// under the folded form, so indexers return releases named that way, while the original
        /// spelling can also appear in the wild.
        /// </summary>
        public static List<string> GetParseTerms(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return new List<string>();
            }

            var terms = new List<string> { title.CleanSeriesTitle() };
            var folded = NormalizeSearchTerm(title).CleanSeriesTitle();

            if (folded != terms[0])
            {
                terms.Add(folded);
            }

            return terms.Where(t => t.IsNotNullOrWhiteSpace()).ToList();
        }

        /// <summary>
        /// Folds typography to Latin-1 so GetSceneNames' IsEnglish filter keeps the term.
        /// Ligatures need explicit mapping - they have no canonical decomposition, so RemoveAccent
        /// leaves them - while spaces, dashes and quotes fold by Unicode category rather than a
        /// hand-written list, which kept missing characters real French typography uses (narrow
        /// no-break space, non-breaking hyphen).
        /// </summary>
        public static string NormalizeSearchTerm(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return title;
            }

            var folded = title.Replace("\u0153", "oe")
                              .Replace("\u0152", "Oe")
                              .Replace("\u00E6", "ae")
                              .Replace("\u00C6", "Ae")
                              .Replace("\u00DF", "ss")
                              .Replace("\u2026", "...");

            folded = SpaceSeparatorRegex.Replace(folded, " ");
            folded = DashPunctuationRegex.Replace(folded, "-");
            folded = QuotePunctuationRegex.Replace(folded, "'");

            return folded.Trim();
        }

        private void UpdateMappings()
        {
            _logger.Info("Updating Scene mappings");

            _updatedAfterStartup = true;

            foreach (var sceneMappingProvider in _sceneMappingProviders)
            {
                try
                {
                    var mappings = sceneMappingProvider.GetSceneMappings();

                    if (mappings.Any())
                    {
                        _repository.Clear(sceneMappingProvider.GetType().Name);

                        mappings.RemoveAll(sceneMapping =>
                        {
                            if (sceneMapping.Title.IsNullOrWhiteSpace() ||
                                sceneMapping.SearchTerm.IsNullOrWhiteSpace())
                            {
                                _logger.Warn("Invalid scene mapping found for: {0}, skipping", sceneMapping.TvdbId);
                                return true;
                            }

                            return false;
                        });

                        foreach (var sceneMapping in mappings)
                        {
                            sceneMapping.ParseTerm = sceneMapping.Title.CleanSeriesTitle();
                            sceneMapping.Type = sceneMappingProvider.GetType().Name;
                        }

                        _repository.InsertMany(mappings.ToList());
                    }
                    else
                    {
                        _logger.Warn("Received empty list of mapping. will not update");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to Update Scene Mappings");
                }
            }

            RefreshCache();

            _eventAggregator.PublishEvent(new SceneMappingsUpdatedEvent());
        }

        private List<SceneMapping> FindMappings(string seriesTitle, string releaseTitle)
        {
            if (_getTvdbIdCache.Count == 0)
            {
                RefreshCache();
            }

            var candidates = _getTvdbIdCache.Find(seriesTitle.CleanSeriesTitle());

            if (candidates == null)
            {
                return null;
            }

            candidates = FilterSceneMappings(candidates, releaseTitle);

            if (candidates.Count <= 1)
            {
                return candidates;
            }

            var exactMatch = candidates.OrderByDescending(v => v.SeasonNumber)
                                       .Where(v => v.Title == seriesTitle)
                                       .ToList();

            if (exactMatch.Any())
            {
                return exactMatch;
            }

            var closestMatch = candidates.OrderBy(v => seriesTitle.LevenshteinDistance(v.Title, 10, 1, 10))
                                         .ThenByDescending(v => v.SeasonNumber)
                                         .First();

            return candidates.Where(v => v.Title == closestMatch.Title).ToList();
        }

        private void RefreshCache()
        {
            var mappings = _repository.All().ToList();

            _getTvdbIdCache.Update(mappings.GroupBy(v => v.ParseTerm).ToDictionary(v => v.Key, v => v.ToList()));
            _findByTvdbIdCache.Update(mappings.GroupBy(v => v.TvdbId).ToDictionary(v => v.Key.ToString(), v => v.ToList()));
        }

        private List<SceneMapping> FilterSceneMappings(List<SceneMapping> candidates, string releaseTitle)
        {
            var filteredCandidates = candidates.Where(v => v.FilterRegex.IsNotNullOrWhiteSpace()).ToList();
            var normalCandidates = candidates.Except(filteredCandidates).ToList();

            if (releaseTitle.IsNullOrWhiteSpace())
            {
                return normalCandidates;
            }

            var simpleTitle = Parser.Parser.SimplifyTitle(releaseTitle);

            filteredCandidates = filteredCandidates.Where(v => Regex.IsMatch(simpleTitle, v.FilterRegex)).ToList();

            if (filteredCandidates.Any())
            {
                return filteredCandidates;
            }

            return normalCandidates;
        }

        private List<SceneMapping> FilterSceneMappings(List<SceneMapping> candidates, int sceneSeasonNumber)
        {
            var filteredCandidates = candidates.Where(v => (v.SceneSeasonNumber ?? -1) != -1 && (v.SeasonNumber ?? -1) != -1).ToList();
            var normalCandidates = candidates.Except(filteredCandidates).ToList();

            if (sceneSeasonNumber == -1)
            {
                return normalCandidates;
            }

            if (filteredCandidates.Any())
            {
                filteredCandidates = filteredCandidates.Where(v => v.SceneSeasonNumber <= sceneSeasonNumber)
                                                       .GroupBy(v => v.Title)
                                                       .Select(d => d.OrderByDescending(v => v.SceneSeasonNumber)
                                                                     .ThenByDescending(v => v.SeasonNumber)
                                                                     .First())
                                                       .ToList();

                return filteredCandidates;
            }

            return normalCandidates;
        }

        private bool IsEnglish(string title)
        {
            return title.All(c => c <= 255);
        }

        public void Handle(SeriesRefreshStartingEvent message)
        {
            if (message.ManualTrigger && (_findByTvdbIdCache.IsExpired(TimeSpan.FromMinutes(1)) || !_updatedAfterStartup))
            {
                UpdateMappings();
            }
        }

        public void Handle(SeriesAddedEvent message)
        {
            if (!_updatedAfterStartup)
            {
                UpdateMappings();
            }
        }

        public void Handle(SeriesImportedEvent message)
        {
            if (!_updatedAfterStartup)
            {
                UpdateMappings();
            }
        }

        public void Execute(UpdateSceneMappingCommand message)
        {
            UpdateMappings();
        }
    }
}

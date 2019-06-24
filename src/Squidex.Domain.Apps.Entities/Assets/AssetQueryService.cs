﻿// ==========================================================================
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex UG (haftungsbeschraenkt)
//  All rights reserved. Licensed under the MIT license.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.OData;
using Squidex.Domain.Apps.Core.Tags;
using Squidex.Domain.Apps.Entities.Assets.Edm;
using Squidex.Domain.Apps.Entities.Assets.Queries;
using Squidex.Domain.Apps.Entities.Assets.Repositories;
using Squidex.Infrastructure;
using Squidex.Infrastructure.Queries;
using Squidex.Infrastructure.Queries.OData;

namespace Squidex.Domain.Apps.Entities.Assets
{
    public sealed class AssetQueryService : IAssetQueryService
    {
        private readonly ITagService tagService;
        private readonly IAssetEnricher assetEnricher;
        private readonly IAssetRepository assetRepository;
        private readonly AssetOptions options;

        public int DefaultPageSizeGraphQl
        {
            get { return options.DefaultPageSizeGraphQl; }
        }

        public AssetQueryService(
            ITagService tagService,
            IAssetEnricher assetEnricher,
            IAssetRepository assetRepository,
            IOptions<AssetOptions> options)
        {
            Guard.NotNull(tagService, nameof(tagService));
            Guard.NotNull(assetEnricher, nameof(assetEnricher));
            Guard.NotNull(assetRepository, nameof(assetRepository));
            Guard.NotNull(options, nameof(options));

            this.tagService = tagService;
            this.assetEnricher = assetEnricher;
            this.assetRepository = assetRepository;
            this.options = options.Value;
        }

        public async Task<IEnrichedAssetEntity> FindAssetAsync( Guid id)
        {
            var asset = await assetRepository.FindAssetAsync(id);

            if (asset != null)
            {
                return await assetEnricher.EnrichAsync(asset);
            }

            return null;
        }

        public async Task<IReadOnlyList<IEnrichedAssetEntity>> QueryByHashAsync(Guid appId, string hash)
        {
            Guard.NotNull(hash, nameof(hash));

            var assets = await assetRepository.QueryByHashAsync(appId, hash);

            var enriched = await assetEnricher.EnrichAsync(assets);

            return enriched;
        }

        public async Task<IResultList<IEnrichedAssetEntity>> QueryAsync(QueryContext context, Q query)
        {
            Guard.NotNull(context, nameof(context));
            Guard.NotNull(query, nameof(query));

            IResultList<IAssetEntity> assets;

            if (query.Ids != null)
            {
                assets = await assetRepository.QueryAsync(context.App.Id, new HashSet<Guid>(query.Ids));
                assets = Sort(assets, query.Ids);
            }
            else
            {
                var parsedQuery = ParseQuery(context, query.ODataQuery);

                assets = await assetRepository.QueryAsync(context.App.Id, parsedQuery);
            }

            var enriched = await assetEnricher.EnrichAsync(assets);

            return ResultList.Create<IEnrichedAssetEntity>(assets.Total, enriched);
        }

        private static IResultList<IAssetEntity> Sort(IResultList<IAssetEntity> assets, IReadOnlyList<Guid> ids)
        {
            var sorted = ids.Select(id => assets.FirstOrDefault(x => x.Id == id)).Where(x => x != null);

            return ResultList.Create(assets.Total, sorted);
        }

        private Query ParseQuery(QueryContext context, string query)
        {
            try
            {
                var result = EdmAssetModel.Edm.ParseQuery(query).ToQuery();

                if (result.Filter != null)
                {
                    result.Filter = FilterTagTransformer.Transform(result.Filter, context.App.Id, tagService);
                }

                if (result.Sort.Count == 0)
                {
                    result.Sort.Add(new SortNode(new List<string> { "lastModified" }, SortOrder.Descending));
                }

                if (result.Take == long.MaxValue)
                {
                    result.Take = options.DefaultPageSize;
                }
                else if (result.Take > options.MaxResults)
                {
                    result.Take = options.MaxResults;
                }

                return result;
            }
            catch (NotSupportedException)
            {
                throw new ValidationException("OData operation is not supported.");
            }
            catch (ODataException ex)
            {
                throw new ValidationException($"Failed to parse query: {ex.Message}", ex);
            }
        }
    }
}

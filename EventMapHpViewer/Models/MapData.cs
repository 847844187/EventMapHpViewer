using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Codeplex.Data;
using Grabacr07.KanColleWrapper;
using Nekoxy;

namespace EventMapHpViewer.Models
{
    public class MapData
    {
        public int Id { get; set; }
        public int IsCleared { get; set; }
        public int IsExBoss { get; set; }
        public int DefeatCount { get; set; }
        public Eventmap Eventmap { get; set; }

        public MapInfo Master => Maps.MapInfos[this.Id];

        public string MapNumber => this.Master.MapAreaId + "-" + this.Master.IdInEachMapArea;

        public string Name => this.Master.Name;

        public string AreaName => this.Master.MapArea.Name;

        public GaugeType GaugeType => this.Eventmap?.GaugeType ?? GaugeType.Normal;

        public int Max
        {
            get
            {
                if (this.IsExBoss == 1) return this.Master.RequiredDefeatCount;
                return this.Eventmap?.MaxMapHp ?? 1;
            }
        }

        public int Current
        {
            get
            {
                if (this.IsExBoss == 1) return this.Master.RequiredDefeatCount - this.DefeatCount;  //�Q�[�W�L��ʏ�C��
                return this.Eventmap?.NowMapHp /*�C�x���g�C��*/?? 1 /*�Q�[�W�����ʏ�C��*/;
            }
        }

        private int[] remoteBossHpCache;

        /// <summary>
        /// �c�񐔁B�A���̏ꍇ��A�����̎c�񐔁B
        /// </summary>
        public async Task<int> GetRemainingCount(bool useCache = false)
        {
            if (this.IsCleared == 1) return 0;

            if (this.IsExBoss == 1) return this.Current;    //�Q�[�W�L��ʏ�C��

            if (this.Eventmap == null) return 1;    //�Q�[�W�����ʏ�C��

            if (this.Eventmap.GaugeType == GaugeType.Transport)
            {
                var capacityA = KanColleClient.Current.Homeport.Organization.TransportationCapacity();
                if (capacityA == 0) return int.MaxValue;  //�Q�[�W����Ȃ�
                return (int)Math.Ceiling((double)this.Current / capacityA);
            }

            if (this.Eventmap.SelectedRank == 0) return -1; //��Փx���I��

            if (!useCache)
                this.remoteBossHpCache = await GetEventBossHp(this.Id, this.Eventmap.SelectedRank);

            var remoteBossHp = this.remoteBossHpCache;
            if (remoteBossHp != null && remoteBossHp.Any())
                return this.CalculateRemainingCount(remoteBossHp);   //�C�x���g�C��(�����[�g�f�[�^)

            try
            {
                // �����[�g�f�[�^���Ȃ��ꍇ�A���[�J���f�[�^���g��
                return this.CalculateRemainingCount(eventBossHpDictionary[this.Eventmap.SelectedRank][this.Id]);   //�C�x���g�C��
            }
            catch (KeyNotFoundException)
            {
                return -1;  //���Ή�
            }
        }

        private int CalculateRemainingCount(int[] bossHPs)
        {
            var lastBossHp = bossHPs.Last();
            var normalBossHp = bossHPs.First();
            if (this.Current <= lastBossHp) return 1;   //�Ō��1��
            return (int)Math.Ceiling((double)(this.Current - lastBossHp) / normalBossHp) + 1;
        }

        /// <summary>
        /// �A���Q�[�W��S�������̎c��
        /// </summary>
        public int RemainingCountTransportS
        {
            get
            {
                if (this.Eventmap?.GaugeType != GaugeType.Transport) return -1;
                var capacity = KanColleClient.Current.Homeport.Organization.TransportationCapacity(true);
                if (capacity == 0) return int.MaxValue;  //�Q�[�W����Ȃ�
                return (int)Math.Ceiling((double)this.Current / capacity);
            }
        }

        /// <summary>
        /// �͂����p�f�[�^�E�����N����{�X�����擾����B
        /// �擾�ł��Ȃ������ꍇ�� null ��Ԃ��B
        /// </summary>
        /// <param name="mapId"></param>
        /// <param name="rank"></param>
        /// <returns></returns>
        private static async Task<int[]> GetEventBossHp(int mapId, int rank)
        {
            using (var client = new HttpClient(GetProxyConfiguredHandler()))
            {
                client.DefaultRequestHeaders
                    .TryAddWithoutValidation("User-Agent", $"{MapHpViewer.title}/{MapHpViewer.version}");
                try {
                    // rank �̌���"1"�̓T�[�o�[��蓮�����e�f�[�^���������邩�ǂ����̃t���O
                    var response = await client.GetAsync($"https://kctadil.azurewebsites.net/map/exboss/{mapId}/{rank}/1");
                    if (!response.IsSuccessStatusCode)
                    {
                        // 200 ����Ȃ�����
                        return null;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    Raw.map_exboss[] parsed = DynamicJson.Parse(json);
                    if (parsed == null
                        || !parsed.Any(x => x.isLast)
                        || !parsed.Any(x => !x.isLast))
                    {
                        // �f�[�^�������Ă��Ȃ�
                        return null;
                    }
                    return parsed
                        .OrderBy(x => x.isLast) // �ŏI�Ґ������ɗ���悤�ɂ���
                        .Select(x => x.ship.maxhp)
                        .ToArray();
                }
                catch (HttpRequestException)
                {
                    // HTTP ���N�G�X�g�Ɏ��s����
                    return null;
                }
            }
        }

        /// <summary>
        /// �{�̂̃v���L�V�ݒ��g�ݍ���HttpClientHandler��Ԃ��B
        /// </summary>
        /// <returns></returns>
        private static HttpClientHandler GetProxyConfiguredHandler()
        {
            switch (HttpProxy.UpstreamProxyConfig.Type)
            {
                case ProxyConfigType.DirectAccess:
                    return new HttpClientHandler
                    {
                        UseProxy = false
                    };
                case ProxyConfigType.SpecificProxy:
                    var settings = KanColleClient.Current.Proxy.UpstreamProxySettings;
                    var host = settings.IsUseHttpProxyForAllProtocols ? settings.HttpHost : settings.HttpsHost;
                    var port = settings.IsUseHttpProxyForAllProtocols ? settings.HttpPort : settings.HttpsPort;
                    if (string.IsNullOrWhiteSpace(host))
                    {
                        return new HttpClientHandler { UseProxy = false };
                    }
                    else
                    {
                        return new HttpClientHandler
                        {
                            UseProxy = true,
                            Proxy = new WebProxy($"{host}:{port}"),
                        };
                    }
                case ProxyConfigType.SystemProxy:
                    return new HttpClientHandler();
                default:
                    return new HttpClientHandler();
            }
        }

        /// <summary>
        /// �蓮�����e�f�[�^�p�B
        /// ������폜����錩���݁B
        /// </summary>
        private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<int, int[]>> eventBossHpDictionary
            = new Dictionary<int, IReadOnlyDictionary<int, int[]>>
            {
                { //��Փx���I��
                    0, new Dictionary<int, int[]>
                    {
                    }
                },
                { //��
                    1, new Dictionary<int, int[]>
                    {
                        { 331, new[] { 110 } },
                        { 332, new[] { 600, 380 } },
                        { 333, new[] { 350, 370 } },
                    }
                },
                { //��
                    2, new Dictionary<int, int[]>
                    {
                        { 331, new[] { 110, 130 } },
                        { 332, new[] { 600, 430 } },
                        { 333, new[] { 350, 380 } },
                    }
                },
                { //�b
                    3, new Dictionary<int, int[]>
                    {
                        { 331, new[] { 130, 160 } },
                        { 332, new[] { 600, 480 } },
                        { 333, new[] { 350, 390 } },
                    }
                },
            };
    }
}
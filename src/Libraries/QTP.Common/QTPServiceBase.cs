using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using QTP.Common.Infrastructure;
using QTP.Common.Models;
using QTP.Common.Plugins;
using System.Threading.Channels;

namespace QTP.Common
{
    public abstract class QTPServiceBase : IQTPService
    {
        public abstract Task CloseBrowserProcess(string uniqueId);
        public abstract string Title { get; }
        public abstract Task<(bool, bool, int)> ExecuteWorkerAsync(string uniqueId, JObject taskArgs, CancellationTokenSource linkedCts);
        protected readonly AppSettings _appSettings;
        public string HostName;
        public event EventHandler<PluginLogEventArgs>? OnLogEventHandler;
        public event EventHandler<TaskStateChangedEventArgs>? OnStateChangedEventHandler;
        public event EventHandler<TaskAdWordEventArgs>? OnTaskAdWordEventHandler;

        public QTPServiceBase(AppSettings appSettings)
        {
            this._appSettings = appSettings;
            this.HostName = CommonHelper.GetHostName();
        }


        public virtual void LogWriteLine(string message, LogLevel level = LogLevel.Information)
        {
            OnLogEventHandler?.Invoke(this, new PluginLogEventArgs(message, level));
        }
        public virtual void LogError(string message)
        {
            OnLogEventHandler?.Invoke(this, new PluginLogEventArgs(message, LogLevel.Error));
        }

        public virtual void QTPExecute(StateType type, int id, int count = 1, string? data = null)
        {
            OnStateChangedEventHandler?.Invoke(this, new TaskStateChangedEventArgs(type, id, count, data));
        }

        /// <summary>
        /// 执行
        /// </summary>
        /// <param name="value"></param>
        public virtual void QTPExecuteStart(int id, int count = 1, string? data = null)
        {
            OnStateChangedEventHandler?.Invoke(this, new TaskStateChangedEventArgs(StateType.Start, id, count, data));
        }

        public void QTPExecuteDSP(int id, int count = 1, string? data = null)
        {
            OnStateChangedEventHandler?.Invoke(this, new TaskStateChangedEventArgs(StateType.DSP, id, count, data));
        }
        /// <summary>
        /// 点击
        /// </summary>
        /// <param name="value"></param>
        public virtual void QTPExecuteClickthrough(int id, int count = 1, string? data = null)
        {
            OnStateChangedEventHandler?.Invoke(this, new TaskStateChangedEventArgs(StateType.Clickthrough, id, count, data));
        }
        /// <summary>
        /// 成功
        /// </summary>
        /// <param name="value"></param>
        public virtual void QTPExecuteSuccess(int id, int count = 1, string? data = null)
        {
            OnStateChangedEventHandler?.Invoke(this, new TaskStateChangedEventArgs(StateType.Success, id, count, data));
        }
        /// <summary>
        /// 失败
        /// </summary>
        /// <param name="value"></param>
        public virtual void QTPExecuteFailure(int id, int count = 1, string? data = null)
        {
            OnStateChangedEventHandler?.Invoke(this, new TaskStateChangedEventArgs(StateType.Failure, id, count, data));
        }
        /// <summary>
        /// 完成
        /// </summary>
        /// <param name="value"></param>
        public virtual void QTPExecuteComplete(int id, int count = 1, string? data = null)
        {
            OnStateChangedEventHandler?.Invoke(this, new TaskStateChangedEventArgs(StateType.Complete, id, count, data));
        }

        public virtual void X5Secdata(int id, int count = 0, string? data = null)
        {
            OnStateChangedEventHandler?.Invoke(this, new TaskStateChangedEventArgs(StateType.X5Sec, id, count, data));
        }

        public virtual void QTPUploadAdWord(string type, string word)
        {
            OnTaskAdWordEventHandler?.Invoke(this, new TaskAdWordEventArgs(type, word));
        }






        static SemaphoreSlim _mutex = new SemaphoreSlim(1);
        public async Task<JObject> UpdateTaskStatusAsync(int id, string url, string type = "start", int count = 1)
        {
            try
            {
                await _mutex.WaitAsync();
                using (var client = new HttpClient())
                {
                    HttpResponseMessage response = await client.GetAsync($"{_appSettings.TaskApiUrl}?action=update-v2&id={id}&url={System.Web.HttpUtility.UrlEncode(url)}&hostName={System.Web.HttpUtility.UrlEncode(this.HostName)}&taskName={System.Web.HttpUtility.UrlEncode(_appSettings.TaskName)}&type={type}&count={count}&_t={System.DateTime.Now.Ticks}");
                    response.EnsureSuccessStatusCode();
                    var content = await response.Content.ReadAsStringAsync();
                    return JObject.Parse(content);
                }
            }
            catch (Exception ex)
            {


            }
            finally
            {
                _mutex.Release();
            }
            return null;

        }

        public async Task<JObject> GetTaskStatusAsync(int id, string url)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    HttpResponseMessage response = await client.GetAsync($"{_appSettings.TaskApiUrl}?action=get-status-v2&id={id}&url={System.Web.HttpUtility.UrlEncode(url)}&_t={System.DateTime.Now.Ticks}");
                    response.EnsureSuccessStatusCode();
                    //return JObject.Parse(await response.Content.ReadAsStringAsync());
                    var content = await response.Content.ReadAsStringAsync();
                    return JObject.Parse(content);
                }
            }
            catch (Exception ex)
            {


            }
            return null;
        }
        public async Task<string> AddHotKWAsync(string q)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    //action = addkw & q = 2
                    HttpResponseMessage response = await client.GetAsync($"{_appSettings.TaskApiUrl}?action=addkw&q={System.Web.HttpUtility.UrlEncode(q)}&_t={System.DateTime.Now.Ticks}");
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {

            }
            finally
            {

            }
            return null;

        }
        public async Task<string> AddHotKWAsync(string name, string q,bool cleaningWord = true,string category="default")
        {
           if(!cleaningWord) category = "default";

            try
            {
                using (var client = new HttpClient())
                {
                    HttpResponseMessage response = await client.GetAsync($"{_appSettings.TaskApiUrl}?action=addsmkw&category={System.Web.HttpUtility.UrlEncode(category)}&name={System.Web.HttpUtility.UrlEncode(name)}&q={System.Web.HttpUtility.UrlEncode(q)}&_t={System.DateTime.Now.Ticks}");
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {

            }
            finally
            {

            }
            return null;

        }


 
    }
}

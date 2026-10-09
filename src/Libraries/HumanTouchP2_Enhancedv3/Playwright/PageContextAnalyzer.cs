using Microsoft.Playwright;
using System;
using System.Threading.Tasks;

namespace PlaywrightHumanInput
{
    public sealed class PageContextAnalyzer
    {
        private const string SnapshotScript = @"() => {
            const de = document.documentElement;
            const body = document.body;
            const text = (body?.innerText || '').replace(/\s+/g, ' ').trim();
            const scrollTop = window.scrollY || de?.scrollTop || body?.scrollTop || 0;
            const scrollHeight = Math.max(
                de?.scrollHeight || 0,
                body?.scrollHeight || 0,
                de?.clientHeight || 0);
            const clientHeight = window.innerHeight || de?.clientHeight || 0;

            return {
                TextChars: Math.min(text.length, 20000),
                Images: document.images?.length || 0,
                Videos: document.querySelectorAll('video').length,
                Links: document.links?.length || 0,
                Buttons: document.querySelectorAll('button,[role=button]').length,
                Inputs: document.querySelectorAll('input,textarea,select').length,
                ScrollTop: scrollTop,
                ScrollHeight: scrollHeight,
                ClientHeight: clientHeight,
                ViewportWidth: window.innerWidth || de?.clientWidth || 0,
                ViewportHeight: clientHeight
            };
        }";

        public async Task<PageContextSnapshot?> CaptureAsync(IPage page, System.Threading.CancellationToken token = default)
        {
            if (page == null || page.IsClosed)
                return null;

            try
            {
                return await page.EvaluateAsync<PageContextSnapshot>(SnapshotScript).WaitAsync(TimeSpan.FromSeconds(2), token);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // 页面跳转/关闭/跨文档切换期间上下文分析失败不应影响触摸主流程。
                return null;
            }
        }
    }
}

import { Link } from 'react-router-dom';
import { Settings, Smartphone, Upload, Users } from 'lucide-react';
import { Card, Loading, Notice } from '../components/ui';
import { Avatar } from '../components/truth/MemberPicker';
import { groupRoleLabel } from '../lib/status';
import { LEADER_ROLES } from '../lib/truth';
import { PageHeader, TruthProvider } from './truth/TruthProvider';
import { SettingsTab } from './truth/SettingsTab';

import { t } from '../i18n';
/**
 * /mipangilio — mahali PAMOJA pa kanuni za kikundi.
 *
 * Mipangilio ya zamani (GroupPolicies: mchango, tarehe, neema, faini, kianzio,
 * "mkakati wa kugawa deni", n.k.) iliondolewa kwa sababu ilikuwa nakala ya pili
 * ya kanuni zilezile — kubadilisha moja bila nyingine kulizalisha ripoti
 * zinazopingana. Sasa Injini ya Ukweli inasoma kanuni kutoka hapa pekee.
 */
export function SettingsPage() {
  return (
    <TruthProvider>
      {(ctx) => {
        const leaders = ctx.members.filter((m) => LEADER_ROLES.has(m.role));
        const withoutPhone = ctx.members.filter((m) => m.phones.length === 0).length;
        return (
          <div className="space-y-6">
            <PageHeader
              eyebrow={t("Mipangilio")}
              icon={Settings}
              title={ctx.groupName}
              description={t("Kanuni hizi ndizo pekee zinazotumika kuhesabu michango, faini na kianzio cha kila mwanachama. Ukibadilisha, historia yote inahesabiwa upya papo hapo.")}
            />

            {ctx.bootError && <Notice tone="overdue">{ctx.bootError}</Notice>}
            {!ctx.booted ? (
              <Loading />
            ) : (
              <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
                <div className="min-w-0">
                  {!ctx.settings && (
                    <div className="mb-4">
                      <Notice tone="warn" title={t("Hatua ya kwanza: weka kanuni")}>
                        {t("Injini haiwezi kuhesabu taarifa yoyote kabla kanuni hazijahifadhiwa.")}
                      </Notice>
                    </div>
                  )}
                  <SettingsTab />
                </div>

                <aside className="space-y-4 lg:sticky lg:top-24">
                  <Card title={t("Kikundi")} bodyClassName="p-4">
                    <dl className="grid grid-cols-2 gap-3 text-sm">
                      <div>
                        <dt className="text-xs text-ink-600">{t("Wanachama")}</dt>
                        <dd className="figure text-lg font-semibold text-ink-950">{ctx.members.length}</dd>
                      </div>
                      <div>
                        <dt className="text-xs text-ink-600">{t("Hawana namba")}</dt>
                        <dd className={`figure text-lg font-semibold ${withoutPhone > 0 ? 'text-standing-warn' : 'text-standing-good'}`}>
                          {withoutPhone}
                        </dd>
                      </div>
                    </dl>
                    <div className="mt-4 flex flex-col gap-2">
                      <Link
                        to="/ukweli/simu"
                        className="inline-flex items-center gap-2 rounded-lg border border-ink-100 px-3 py-2 text-sm text-ink-800 hover:bg-ink-50"
                      >
                        <Smartphone size={15} />{' '}{t("Namba za simu za wanachama")}
                      </Link>
                      <Link
                        to="/ukweli/import"
                        className="inline-flex items-center gap-2 rounded-lg border border-ink-100 px-3 py-2 text-sm text-ink-800 hover:bg-ink-50"
                      >
                        <Upload size={15} />{' '}{t("Import taarifa ya M-Koba")}
                      </Link>
                    </div>
                  </Card>

                  <Card title={t("Viongozi")} subtitle={t("Maker-checker: kiongozi mmoja anaingiza, mwingine anaidhinisha.")} bodyClassName="p-2">
                    {leaders.length === 0 ? (
                      <p className="px-2 py-3 text-sm text-ink-600">{t("Hakuna viongozi waliosajiliwa.")}</p>
                    ) : (
                      <ul>
                        {leaders.map((m) => (
                          <li key={m.memberId} className="flex items-center gap-2.5 rounded-lg px-2 py-2">
                            <Avatar name={m.fullName} size="sm" />
                            <div className="min-w-0 flex-1">
                              <div className="truncate text-sm font-medium text-ink-950">{m.fullName}</div>
                              <div className="text-xs text-ink-600">{groupRoleLabel(m.role)}</div>
                            </div>
                          </li>
                        ))}
                      </ul>
                    )}
                    {leaders.length < 2 && (
                      <div className="p-2">
                        <Notice tone="warn">
                          {t("Kuidhinisha malipo ya mkononi kunahitaji viongozi")}{' '}<b>{t("wawili")}</b>{' '}{t("tofauti.")}
                        </Notice>
                      </div>
                    )}
                  </Card>

                  <p className="flex gap-2 px-1 text-xs text-ink-600">
                    <Users size={14} className="mt-0.5 shrink-0" />
                    {t("Kila badiliko la kanuni linaandikwa kwenye kumbukumbu za ukaguzi (nani, lini, nini).")}
                  </p>
                </aside>
              </div>
            )}
          </div>
        );
      }}
    </TruthProvider>
  );
}
